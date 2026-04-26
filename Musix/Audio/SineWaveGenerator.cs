namespace Musix.Audio;

internal static class SineWaveGenerator
{
    internal static float[] Generate(double frequencyHz, int sampleRate, int channels, int durationSeconds)
    {
        int samplesPerChannel = sampleRate * durationSeconds;
        float[] buffer = new float[samplesPerChannel * channels];

        for (int i = 0; i < samplesPerChannel; i++)
        {
            float sample = (float)Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate);
            for (int c = 0; c < channels; c++)
                buffer[i * channels + c] = sample;
        }

        return buffer;
    }
}
