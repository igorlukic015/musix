using Concentus;
using Concentus.Enums;

namespace Musix.Audio;

internal sealed class OpusAudioEncoder
{
    private readonly IOpusEncoder _encoder;
    private readonly byte[] _packetBuffer = new byte[4000];

    internal OpusAudioEncoder(int sampleRate, int channels, int bitrate)
    {
        _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO, null!);
        _encoder.Bitrate = bitrate;
    }

    // Encodes one full Opus frame (frameSize samples per channel, interleaved).
    // Returns the number of bytes written into the internal packet buffer.
    internal int Encode(ReadOnlySpan<float> pcm, int frameSize)
    {
        return _encoder.Encode(pcm, frameSize, _packetBuffer.AsSpan(), _packetBuffer.Length);
    }
}
