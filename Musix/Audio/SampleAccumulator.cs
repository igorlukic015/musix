namespace Musix.Audio;

// Accumulates resampled float samples and dispenses them in fixed-size chunks.
// Incoming quantum sizes are irregular (~960 stereo floats at 48 kHz after
// resampling from 44100 Hz); Opus requires exactly frameSize samples per channel.
internal sealed class SampleAccumulator
{
    private readonly List<float> _buffer = new();
    private readonly int _frameFloats; // frameSize * channels

    internal SampleAccumulator(int frameSize, int channels)
    {
        _frameFloats = frameSize * channels;
    }

    internal void Push(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
            _buffer.Add(sample);
    }

    // Returns true and fills frame with exactly _frameFloats samples when the
    // buffer holds enough. RemoveRange shifts the list — acceptable because the
    // buffer stays small (a few thousand floats between reads).
    internal bool TryDequeue(float[] frame)
    {
        if (_buffer.Count < _frameFloats)
            return false;

        _buffer.CopyTo(0, frame, 0, _frameFloats);
        _buffer.RemoveRange(0, _frameFloats);
        return true;
    }
}
