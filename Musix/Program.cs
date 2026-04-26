using Concentus;
using Concentus.Enums;
using Musix.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();

if (sessions.Count == 0)
{
    Console.WriteLine("No active audio sessions found. Play some audio and try again.");
    return;
}

for (int i = 0; i < sessions.Count; i++)
{
    Console.WriteLine($"  [{i}] {sessions[i].ProcessName} (PID {sessions[i].ProcessId})");
}

Console.Write("\nEnter index to capture (or press Enter for 0): ");
string? input = Console.ReadLine();
int selectedIndex = int.TryParse(input, out int parsed) ? parsed : 0;

AudioSession target = sessions[selectedIndex];
Console.WriteLine($"\nActivating process loopback for: {target.ProcessName} (PID {target.ProcessId})");

using ProcessLoopbackCapture capture = new();
await capture.InitializeAsync(target.ProcessId);

int channels = capture.WaveFormat.Channels;
Console.WriteLine($"Source format: {capture.WaveFormat.SampleRate} Hz, {channels} ch");

using FrameOutputNode frameOutput = new(capture);

// Stage 1 — resample device rate → 48 kHz.
FrameSampleProvider frameProvider = new(capture.WaveFormat.SampleRate, channels);
WdlResamplingSampleProvider resampler = new(frameProvider, 48000);
float[] resampledBuffer = new float[960 * channels];

// Stage 2 — accumulate resampled quanta until we have a full Opus frame.
const int opusFrameSize = 960; // samples per channel
SampleAccumulator accumulator = new(opusFrameSize, channels);
float[] opusFrame = new float[opusFrameSize * channels];

// Stage 3 — Opus encoder + local monitor decoder.
IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, channels, OpusApplication.OPUS_APPLICATION_AUDIO, null!);
encoder.Bitrate = 128000;
byte[] packetBuffer = new byte[4000];

IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(48000, channels, null!);
float[] decodedFrame = new float[opusFrameSize * channels];

// Stage 4 — playback via WasapiOut.
WaveFormat playbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
BufferedWaveProvider playbackBuffer = new(playbackFormat)
{
    BufferDuration = TimeSpan.FromMilliseconds(200),
    DiscardOnBufferOverflow = true,
};

using WasapiOut player = new();
player.Init(playbackBuffer);
player.Play();

frameOutput.QuantumProcessed += (object? _, EventArgs _) =>
{
    if (frameOutput.TryRead(out AudioFrame frame))
    {
        frameProvider.Push(MemoryMarshal.Cast<byte, float>(frame.Data));
    }

    int samplesRead = resampler.Read(resampledBuffer, 0, resampledBuffer.Length);
    if (samplesRead > 0)
    {
        accumulator.Push(resampledBuffer.AsSpan(0, samplesRead));
    }

    while (accumulator.TryDequeue(opusFrame))
    {
        int encodedBytes = encoder.Encode(
            opusFrame.AsSpan(),
            opusFrameSize,
            packetBuffer.AsSpan(),
            packetBuffer.Length);

        int decodedSamples = decoder.Decode(
            packetBuffer.AsSpan(0, encodedBytes),
            decodedFrame.AsSpan(),
            opusFrameSize);

        ReadOnlySpan<byte> pcmBytes = MemoryMarshal.Cast<float, byte>(decodedFrame.AsSpan(0, decodedSamples * channels));
        playbackBuffer.AddSamples(pcmBytes.ToArray(), 0, pcmBytes.Length);

        Console.Write($"\r[{encodedBytes,4} B → {decodedSamples * channels,5} floats] ");
    }
};

capture.StartCapture();
Console.WriteLine("Capturing... Press Enter to stop.");
Console.ReadLine();
capture.StopCapture();
player.Stop();

Console.WriteLine("\nDone.");
