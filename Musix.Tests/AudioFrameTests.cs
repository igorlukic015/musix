using Musix.Audio;
using NAudio.Wave;
using Xunit;

namespace Musix.Tests;

public sealed class AudioFrameTests
{
    // ── Duration — empty / zero-length data ──────────────────────────────────

    [Fact]
    public void Duration_WhenDataIsEmptyArray_ReturnsZero()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        AudioFrame frame = new([], format);

        Assert.Equal(TimeSpan.Zero, frame.Duration);
    }

    [Fact]
    public void Duration_WhenDataIsArrayEmptyInstance_ReturnsZero()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        AudioFrame frame = new(Array.Empty<byte>(), format);

        Assert.Equal(TimeSpan.Zero, frame.Duration);
    }

    // ── Duration — mathematically correct for non-empty buffers ─────────────

    // 32-bit float stereo 48000 Hz: BlockAlign = 4 bytes/sample * 2 channels = 8.
    // 480 frames * 8 bytes = 3840 bytes → duration = 3840 / 8 / 48000 = 0.010 s = 10 ms.
    [Fact]
    public void Duration_FloatStereo48kHz_ReturnsCorrectDuration()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        int frames = 480; // one 10 ms quantum
        byte[] data = new byte[frames * format.BlockAlign];

        AudioFrame frame = new(data, format);

        TimeSpan expected = TimeSpan.FromSeconds((double)data.Length / format.BlockAlign / format.SampleRate);
        Assert.Equal(expected, frame.Duration);
    }

    // 16-bit mono 44100 Hz: BlockAlign = 2. 4410 samples * 2 bytes = 8820 bytes → 0.1 s.
    [Fact]
    public void Duration_PcmMono44100Hz_ReturnsCorrectDuration()
    {
        WaveFormat format = new(44100, 16, 1);
        int frames = 4410;
        byte[] data = new byte[frames * format.BlockAlign];

        AudioFrame frame = new(data, format);

        TimeSpan expected = TimeSpan.FromSeconds((double)data.Length / format.BlockAlign / format.SampleRate);
        Assert.Equal(expected, frame.Duration);
    }

    // Single frame: exactly one sample worth of data should give the smallest non-zero duration.
    [Fact]
    public void Duration_SingleSampleBuffer_ReturnsPositiveDuration()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] data = new byte[format.BlockAlign]; // exactly 1 frame = 8 bytes

        AudioFrame frame = new(data, format);

        Assert.True(frame.Duration > TimeSpan.Zero, "Single-sample buffer must produce a positive duration.");
    }

    // Verify the formula result is strictly positive (not Zero) for any non-empty buffer.
    [Fact]
    public void Duration_NonEmptyBuffer_IsPositive()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] data = new byte[format.BlockAlign * 100];

        AudioFrame frame = new(data, format);

        Assert.True(frame.Duration > TimeSpan.Zero);
    }

    // ── Value semantics ───────────────────────────────────────────────────────

    // AudioFrame is a record struct, so two instances with the same Data reference
    // and Format should be equal.
    [Fact]
    public void AudioFrame_SameDataAndFormat_AreEqual()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] data = new byte[8];

        AudioFrame a = new(data, format);
        AudioFrame b = new(data, format);

        Assert.Equal(a, b);
    }
}
