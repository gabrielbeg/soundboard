using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>
/// Keeps stacked sounds from hard-clipping: linear below the threshold, then a tanh knee up to ±1.
/// </summary>
public sealed class SoftLimiterSampleProvider : ISampleProvider
{
    private const float Threshold = 0.9f;
    private const float Headroom = 1f - Threshold;

    private readonly ISampleProvider _source;

    public SoftLimiterSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = offset; i < offset + read; i++)
        {
            buffer[i] = Limit(buffer[i]);
        }
        return read;
    }

    internal static float Limit(float x)
    {
        float abs = Math.Abs(x);
        if (abs <= Threshold)
        {
            return x;
        }
        float shaped = Threshold + Headroom * MathF.Tanh((abs - Threshold) / Headroom);
        return MathF.CopySign(shaped, x);
    }
}
