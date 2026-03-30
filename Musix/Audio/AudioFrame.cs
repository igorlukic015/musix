using NAudio.Wave;

namespace Musix.Audio;

// A single chunk of PCM audio data produced by an audio graph source node.
// Data contains exactly BytesRecorded bytes of interleaved samples in the
// layout described by Format (e.g. 32-bit float, stereo, 48 kHz).
// Duration is zero for silent or empty frames and positive for frames with real audio.
internal readonly record struct AudioFrame(byte[] Data, WaveFormat Format)
{
    // Time span covered by this frame's sample data.
    // Zero when Data is empty; otherwise Data.Length / BlockAlign / SampleRate.
    public TimeSpan Duration => Data.Length == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)Data.Length / Format.BlockAlign / Format.SampleRate);
}
