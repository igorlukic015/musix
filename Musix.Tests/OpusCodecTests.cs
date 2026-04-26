using Concentus;
using Concentus.Enums;
using Musix.Audio;
using Xunit;

namespace Musix.Tests;

public sealed class OpusCodecTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int FrameSize = 960;                       // 20 ms @ 48 kHz, samples per channel
    private const int FloatsPerFrame = FrameSize * Channels; // 1920

    [Fact]
    public void SineWave_EncodeDecode_RoundtripCloseToOriginal()
    {
        float[] original = SineWaveGenerator.Generate(440, SampleRate, Channels, 1);

        // ── Encode ────────────────────────────────────────────────────────────
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(
            SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO, null!);
        encoder.Bitrate = 128000;

        int lookaheadFloats = encoder.Lookahead * Channels;

        int frameCount = original.Length / FloatsPerFrame; // 50 frames
        List<byte[]> packets = new(frameCount);
        byte[] packetBuffer = new byte[4000];

        for (int frame = 0; frame < frameCount; frame++)
        {
            int encodedBytes = encoder.Encode(
                original.AsSpan(frame * FloatsPerFrame, FloatsPerFrame),
                FrameSize,
                packetBuffer.AsSpan(),
                packetBuffer.Length);
            packets.Add(packetBuffer[..encodedBytes].ToArray());
        }

        int rawBytes = FloatsPerFrame * sizeof(float);
        Console.WriteLine($"First frame: {packets[0].Length} bytes encoded  (raw PCM: {rawBytes} bytes, ratio {(double)rawBytes / packets[0].Length:F1}x)");
        Console.WriteLine($"Encoder lookahead: {encoder.Lookahead} samples/channel ({lookaheadFloats} floats)");

        // ── Decode ────────────────────────────────────────────────────────────
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels, null!);
        float[] decoded = new float[original.Length];

        for (int frame = 0; frame < packets.Count; frame++)
        {
            decoder.Decode(
                packets[frame].AsSpan(),
                decoded.AsSpan(frame * FloatsPerFrame, FloatsPerFrame),
                FrameSize,
                false);
        }

        // ── Print side by side (aligned) ─────────────────────────────────────
        // The decoded stream is shifted forward by lookaheadFloats relative to
        // the original. Align by comparing original[i] with decoded[i + lookaheadFloats].
        Console.WriteLine($"\n  idx | original          | decoded (offset {lookaheadFloats}) | diff");
        for (int i = 0; i < 8; i++)
        {
            float orig = original[i];
            float dec = decoded[lookaheadFloats + i];
            float diff = Math.Abs(orig - dec);
            Console.WriteLine($"  {i,3} | {orig:+0.000000;-0.000000} | {dec:+0.000000;-0.000000} | {diff:0.000000}");
        }

        // ── Assert ────────────────────────────────────────────────────────────
        // Compare one full frame worth of aligned samples, starting well past the
        // lookahead boundary so both encoder and decoder are in steady state.
        // Opus at 128 kbps is lossy; observed per-sample error is ~0.005–0.01.
        const float tolerance = 0.02f;
        int compareStart = FloatsPerFrame; // start one frame into original
        for (int i = compareStart; i < compareStart + FloatsPerFrame; i++)
        {
            int decodedIndex = i + lookaheadFloats;
            if (decodedIndex >= decoded.Length) break;

            Assert.True(
                Math.Abs(original[i] - decoded[decodedIndex]) < tolerance,
                $"Sample {i}: original={original[i]:F6}, decoded={decoded[decodedIndex]:F6}");
        }
    }
}
