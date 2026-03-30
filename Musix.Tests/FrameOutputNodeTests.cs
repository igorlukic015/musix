using Musix.Audio;
using NAudio.Wave;
using System.Reflection;
using System.Threading.Channels;
using Xunit;

namespace Musix.Tests;

// Drives FrameOutputNode without real WASAPI hardware by:
//   1. Constructing ProcessLoopbackCapture (constructor is side-effect-free).
//   2. Setting _waveFormat via reflection so that WaveFormat does not throw.
//   3. Raising DataAvailable via reflection on the backing delegate field so that
//      OnDataAvailable is invoked exactly as it would be from the capture thread.
public sealed class FrameOutputNodeTests
{
    private static readonly WaveFormat TestFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Injects a WaveFormat into the otherwise-uninitialized ProcessLoopbackCapture so
    // that FrameOutputNode.OnDataAvailable can read _source.WaveFormat without throwing.
    private static ProcessLoopbackCapture CreatePrimedCapture()
    {
        ProcessLoopbackCapture capture = new();

        FieldInfo? field = typeof(ProcessLoopbackCapture)
            .GetField("_waveFormat", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        field!.SetValue(capture, TestFormat);

        return capture;
    }

    // Raises the DataAvailable event on capture by invoking the backing delegate directly.
    // This simulates exactly what CaptureLoop does: DataAvailable?.Invoke(this, args).
    private static void RaiseDataAvailable(ProcessLoopbackCapture capture, WaveInEventArgs args)
    {
        FieldInfo? field = typeof(ProcessLoopbackCapture)
            .GetField("DataAvailable", BindingFlags.NonPublic | BindingFlags.Instance);

        // The C# compiler generates a backing field with the same name as the event.
        Assert.NotNull(field);

        EventHandler<WaveInEventArgs>? handler =
            field!.GetValue(capture) as EventHandler<WaveInEventArgs>;

        handler?.Invoke(capture, args);
    }

    // Builds a buffer whose bytes are all non-zero so that HasAudio returns true.
    private static byte[] AudioBuffer(int byteCount)
    {
        byte[] buffer = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            buffer[i] = 0x42;
        }
        return buffer;
    }

    // Builds a buffer whose bytes are all zero (simulates a silent WASAPI quantum).
    private static byte[] SilentBuffer(int byteCount) => new byte[byteCount];

    // ── Audio frame enqueued ──────────────────────────────────────────────────

    [Fact]
    public void TryRead_AfterAudioDataAvailable_ReturnsTrueAndFrameMatchesInput()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        bool read = node.TryRead(out AudioFrame frame);

        Assert.True(read, "TryRead must return true when a non-zero audio buffer was raised.");
        Assert.Equal(input.Length, frame.Data.Length);
        Assert.Equal(input, frame.Data);
    }

    // ── Silent frame not enqueued ─────────────────────────────────────────────

    [Fact]
    public void TryRead_AfterSilentDataAvailable_ReturnsFalse()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        byte[] silent = SilentBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(silent, silent.Length));

        bool read = node.TryRead(out AudioFrame _);

        Assert.False(read, "Silent (all-zero) buffers must not be enqueued.");
    }

    // ── Zero BytesRecorded not enqueued ───────────────────────────────────────

    [Fact]
    public void TryRead_WhenBytesRecordedIsZero_ReturnsFalse()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        byte[] buffer = AudioBuffer(TestFormat.BlockAlign * 10);
        // BytesRecorded = 0, even though the buffer bytes are non-zero.
        RaiseDataAvailable(capture, new WaveInEventArgs(buffer, 0));

        bool read = node.TryRead(out AudioFrame _);

        Assert.False(read, "BytesRecorded == 0 must not produce an enqueued frame.");
    }

    // ── QuantumProcessed fires for audio ──────────────────────────────────────

    [Fact]
    public void QuantumProcessed_FiresWhenAudioDataAvailable()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        int eventCount = 0;
        node.QuantumProcessed += (object? _, EventArgs _) => eventCount++;

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        Assert.Equal(1, eventCount);
    }

    // ── QuantumProcessed fires for silence ────────────────────────────────────

    [Fact]
    public void QuantumProcessed_FiresEvenWhenBufferIsSilent()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        int eventCount = 0;
        node.QuantumProcessed += (object? _, EventArgs _) => eventCount++;

        byte[] silent = SilentBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(silent, silent.Length));

        Assert.Equal(1, eventCount);
    }

    // ── QuantumProcessed fires multiple times ─────────────────────────────────

    [Fact]
    public void QuantumProcessed_FiresOncePerDataAvailableEvent()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        int eventCount = 0;
        node.QuantumProcessed += (object? _, EventArgs _) => eventCount++;

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        Assert.Equal(3, eventCount);
    }

    // ── Data is copied ────────────────────────────────────────────────────────

    [Fact]
    public void Frame_DataIsACopy_MutatingOriginalBufferDoesNotAffectFrame()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        bool read = node.TryRead(out AudioFrame frame);
        Assert.True(read);

        // Mutate the original buffer after the event has been processed.
        byte originalFirstByte = frame.Data[0];
        input[0] = (byte)(input[0] ^ 0xFF);

        Assert.Equal(originalFirstByte, frame.Data[0]);
        Assert.NotSame(input, frame.Data);
    }

    // ── Dispose unsubscribes and stops enqueuing ──────────────────────────────

    [Fact]
    public void TryRead_AfterDispose_ReturnsFalseForSubsequentEvents()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        FrameOutputNode node = new(capture);
        node.Dispose();

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        bool read = node.TryRead(out AudioFrame _);

        Assert.False(read, "After Dispose, DataAvailable events must no longer enqueue frames.");
    }

    [Fact]
    public void QuantumProcessed_AfterDispose_IsNotRaised()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        FrameOutputNode node = new(capture);

        int eventCount = 0;
        node.QuantumProcessed += (object? _, EventArgs _) => eventCount++;

        node.Dispose();

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        Assert.Equal(0, eventCount);
    }

    // ── Dispose completes the channel ─────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_AfterDispose_ThrowsChannelClosedException()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        FrameOutputNode node = new(capture);
        node.Dispose();

        // The channel is completed synchronously by Dispose; ReadAsync on an empty
        // completed channel throws ChannelClosedException immediately.
        await Assert.ThrowsAsync<ChannelClosedException>(
            async () => await node.ReadAsync());
    }

    // ── Dispose is idempotent ─────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        FrameOutputNode node = new(capture);

        Exception? ex = Record.Exception(() =>
        {
            node.Dispose();
            node.Dispose();
            node.Dispose();
        });

        Assert.Null(ex);
    }

    // ── Capacity / DropOldest ─────────────────────────────────────────────────

    [Fact]
    public void Channel_WhenFull_DropsOldestAndRetainsNewest()
    {
        int capacity = 4;
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture, capacity);

        // Fill the channel to capacity. Each buffer has a distinct first-byte value
        // so we can identify which frames survive.
        for (int i = 0; i < capacity; i++)
        {
            byte[] buf = AudioBuffer(TestFormat.BlockAlign);
            buf[0] = (byte)(i + 1); // values 1..4
            RaiseDataAvailable(capture, new WaveInEventArgs(buf, buf.Length));
        }

        // Push one more frame — this should drop the oldest (first-byte = 1).
        byte[] newest = AudioBuffer(TestFormat.BlockAlign);
        newest[0] = 99;
        RaiseDataAvailable(capture, new WaveInEventArgs(newest, newest.Length));

        // Drain the channel. We expect 4 frames; the one with first-byte = 1 must be gone.
        List<byte> firstBytes = [];
        while (node.TryRead(out AudioFrame f))
        {
            firstBytes.Add(f.Data[0]);
        }

        Assert.Equal(capacity, firstBytes.Count);
        Assert.DoesNotContain((byte)1, firstBytes);
        Assert.Contains((byte)99, firstBytes);
    }

    // ── WaveFormat property delegates to source ───────────────────────────────

    [Fact]
    public void WaveFormat_ReturnsSourceWaveFormat()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        WaveFormat format = node.WaveFormat;

        Assert.Equal(TestFormat.SampleRate, format.SampleRate);
        Assert.Equal(TestFormat.Channels, format.Channels);
        Assert.Equal(TestFormat.BitsPerSample, format.BitsPerSample);
    }

    // ── Frame carries the correct WaveFormat ─────────────────────────────────

    [Fact]
    public void Frame_ContainsSourceWaveFormat()
    {
        using ProcessLoopbackCapture capture = CreatePrimedCapture();
        using FrameOutputNode node = new(capture);

        byte[] input = AudioBuffer(TestFormat.BlockAlign * 10);
        RaiseDataAvailable(capture, new WaveInEventArgs(input, input.Length));

        node.TryRead(out AudioFrame frame);

        Assert.Equal(TestFormat.SampleRate, frame.Format.SampleRate);
        Assert.Equal(TestFormat.Channels, frame.Format.Channels);
        Assert.Equal(TestFormat.BitsPerSample, frame.Format.BitsPerSample);
    }
}
