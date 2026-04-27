using Concentus;
using Musix.Network;
using NAudio.Wave;
using System.Net.Sockets;
using System.Runtime.InteropServices;

[DllImport("winmm.dll")] static extern int timeBeginPeriod(int uPeriod);
timeBeginPeriod(1);

const int sampleRate = 48000;
const int channels = 2;
const int opusFrameSize = 960;

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 5000;

Console.WriteLine($"Connecting to {host}:{port}...");

using TcpClient client = new();
await client.ConnectAsync(host, port);
client.NoDelay = true;
Console.WriteLine("Connected. Receiving packets...\n");

NetworkStream stream = client.GetStream();
JitterBuffer jitterBuffer = new();

IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels, null!);
float[] decodedFrame = new float[opusFrameSize * channels];
byte[] pcmByteBuffer = new byte[opusFrameSize * channels * sizeof(float)];

WaveFormat playbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
BufferedWaveProvider playbackBuffer = new(playbackFormat)
{
    BufferDuration = TimeSpan.FromMilliseconds(500),
    DiscardOnBufferOverflow = false,
};

using WasapiOut player = new();
player.Init(playbackBuffer);
player.Play();

using CancellationTokenSource cts = new();

TimeSpan targetBuffer = TimeSpan.FromMilliseconds(150);

Task consumerTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        if (playbackBuffer.BufferedDuration >= targetBuffer)
        {
            try { await Task.Delay(10, cts.Token); }
            catch (OperationCanceledException) { break; }
            continue;
        }

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

        MemoryMarshal.Cast<float, byte>(decodedFrame.AsSpan(0, decodedSamples * channels))
            .CopyTo(pcmByteBuffer.AsSpan());
        playbackBuffer.AddSamples(pcmByteBuffer, 0, decodedSamples * channels * sizeof(float));
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
