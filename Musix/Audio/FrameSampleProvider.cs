using NAudio.Wave;

namespace Musix.Audio;

// Push-pull adapter between the frame pipeline and NAudio's ISampleProvider.
// The capture thread pushes float samples in via Push(); WdlResamplingSampleProvider
// pulls them out via Read() and resamples to the target rate on demand.
internal sealed class FrameSampleProvider : ISampleProvider
{
    private readonly Queue<float> _buffer = new();

    public WaveFormat WaveFormat { get; }

    internal FrameSampleProvider(int sampleRate, int channels)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    internal void Push(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
            _buffer.Enqueue(sample);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count && _buffer.Count > 0)
            buffer[offset + read++] = _buffer.Dequeue();
        return read;
    }
}
