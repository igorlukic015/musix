using NAudio.Wave;
using System.Threading.Channels;

namespace Musix.Audio;

// Connects to a ProcessLoopbackCapture source and exposes its audio as a
// pull-based stream of AudioFrame values.
//
// Internally the node subscribes to DataAvailable and enqueues each packet into
// a bounded Channel. Consumers call TryRead for non-blocking polls or ReadAsync
// for async blocking reads. When the channel is full, the oldest frame is dropped
// so the capture thread is never stalled.
//
// Silent frames (AUDCLNT_BUFFERFLAGS_SILENT — zeroed buffer) are NOT enqueued.
// QuantumProcessed fires for every WASAPI quantum regardless of silence, so a
// handler can call TryRead and use frame.Duration > TimeSpan.Zero as the gate:
// real audio → TryRead succeeds and duration is positive; silence → TryRead fails.
//
// Typical lifecycle:
//   1. new FrameOutputNode(capture)          — wires up the DataAvailable subscription
//   2. capture.StartCapture()                — frames begin flowing into the channel
//   3. QuantumProcessed handler + TryRead    — consumer reacts at quantum rate
//   4. Dispose()                             — unsubscribes and completes the channel
internal sealed class FrameOutputNode : IDisposable
{
    private readonly ProcessLoopbackCapture _source;
    private readonly Channel<AudioFrame> _channel;
    private bool _disposed;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public ChannelReader<AudioFrame> Frames => _channel.Reader;

    // Raised on the capture thread for every WASAPI quantum, whether silent or not.
    // Call TryRead inside the handler to pull the frame; it returns false for silent quanta.
    public event EventHandler? QuantumProcessed;

    public FrameOutputNode(ProcessLoopbackCapture source, int capacity = 256)
    {
        _source = source;
        _channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = false,
        });
        _source.DataAvailable += OnDataAvailable;
    }

    // Non-blocking: returns true and sets frame if one is available, false otherwise.
    public bool TryRead(out AudioFrame frame) => _channel.Reader.TryRead(out frame);

    // Async blocking: waits until a frame is available or cancellation is requested.
    public ValueTask<AudioFrame> ReadAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.DataAvailable -= OnDataAvailable;
        _channel.Writer.TryComplete();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // WASAPI zeroes the buffer for silent packets (AUDCLNT_BUFFERFLAGS_SILENT) and
        // still fires DataAvailable. Only enqueue when real audio is present.
        if (e.BytesRecorded > 0 && HasAudio(e.Buffer, e.BytesRecorded))
        {
            byte[] copy = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
            _channel.Writer.TryWrite(new AudioFrame(copy, _source.WaveFormat));
        }

        QuantumProcessed?.Invoke(this, EventArgs.Empty);
    }

    // Returns true as soon as any non-zero byte is found. Silent frames guaranteed by
    // WASAPI to be fully zeroed, so this correctly distinguishes them from real audio.
    private static bool HasAudio(byte[] buffer, int byteCount)
    {
        for (int i = 0; i < byteCount; i++)
        {
            if (buffer[i] != 0) return true;
        }
        return false;
    }
}
