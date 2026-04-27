using Concentus;
using Musix.Network;
using NAudio.Wave;
using System.Net.Sockets;
using System.Runtime.InteropServices;

const int sampleRate = 48000;
const int channels = 2;
const int opusFrameSize = 960;

Console.WriteLine("Connecting to localhost:5000...");

using TcpClient client = new();
await client.ConnectAsync("127.0.0.1", 5000);
Console.WriteLine("Connected. Receiving packets...\n");

NetworkStream stream = client.GetStream();
JitterBuffer jitterBuffer = new();

IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels, null!);
float[] decodedFrame = new float[opusFrameSize * channels];

WaveFormat playbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
BufferedWaveProvider playbackBuffer = new(playbackFormat)
{
    BufferDuration = TimeSpan.FromMilliseconds(200),
    DiscardOnBufferOverflow = true,
};

using WasapiOut player = new();
player.Init(playbackBuffer);
player.Play();

using CancellationTokenSource cts = new();

Task consumerTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        AudioPacket? packet = jitterBuffer.TryDequeue();

        int decodedSamples;
        if (packet is not null)
        {
            decodedSamples = decoder.Decode(
                packet.Payload.AsSpan(),
                decodedFrame.AsSpan(),
                opusFrameSize);
            Console.WriteLine($"seq={packet.SequenceNumber} size={packet.Payload.Length}B");
        }
        else
        {
            decodedSamples = decoder.Decode(
                ReadOnlySpan<byte>.Empty,
                decodedFrame.AsSpan(),
                opusFrameSize);
        }

        ReadOnlySpan<byte> pcmBytes = MemoryMarshal.Cast<float, byte>(
            decodedFrame.AsSpan(0, decodedSamples * channels));
        playbackBuffer.AddSamples(pcmBytes.ToArray(), 0, pcmBytes.Length);

        try { await Task.Delay(20, cts.Token); }
        catch (OperationCanceledException) { break; }
    }
});

try
{
    while (true)
    {
        AudioPacket packet = await PacketReader.ReadAsync(stream);
        jitterBuffer.Add(packet);
    }
}
catch (EndOfStreamException)
{
    Console.WriteLine("\nHost closed connection.");
}
catch (IOException)
{
    Console.WriteLine("\nConnection lost.");
}

cts.Cancel();
await consumerTask;
player.Stop();
Console.WriteLine("Done.");
