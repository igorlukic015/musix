using Concentus;
using Concentus.Enums;
using Musix.Audio;
using Musix.Host;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

const int port = 5000;
using CancellationTokenSource cts = new();
using AudioBroadcaster broadcaster = new(port);
broadcaster.Start(cts.Token);
Console.WriteLine($"Listening for listeners on port {port}");

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

FrameSampleProvider frameProvider = new(capture.WaveFormat.SampleRate, channels);
WdlResamplingSampleProvider resampler = new(frameProvider, 48000);
float[] resampledBuffer = new float[960 * channels];

const int opusFrameSize = 960;
SampleAccumulator accumulator = new(opusFrameSize, channels);
float[] opusFrame = new float[opusFrameSize * channels];

IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, channels, OpusApplication.OPUS_APPLICATION_AUDIO, null!);
encoder.Bitrate = 128000;
byte[] packetBuffer = new byte[4000];

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

        broadcaster.SendFrame(packetBuffer[..encodedBytes]);
    }
};

capture.StartCapture();
Console.WriteLine("Capturing... Press Enter to stop.");
Console.ReadLine();
capture.StopCapture();
cts.Cancel();

Console.WriteLine("\nDone.");
