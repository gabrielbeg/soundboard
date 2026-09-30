using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>
/// One playing instance of a <see cref="CachedSound"/> feeding one mixer.
/// Returning fewer samples than requested makes the MixingSampleProvider drop it.
/// </summary>
internal sealed class SoundVoice : ISampleProvider
{
    private readonly CachedSound _sound;
    private readonly Func<float> _gain;
    private long _position;
    private volatile bool _stopped;

    public SoundVoice(CachedSound sound, Func<float> gain)
    {
        _sound = sound;
        _gain = gain;
    }

    public WaveFormat WaveFormat => AudioFormat.Mix;

    public bool IsFinished => _stopped || Interlocked.Read(ref _position) >= _sound.Samples.Length;

    /// <summary>Fraction played, 0–1.</summary>
    public double Progress =>
        _sound.Samples.Length == 0 ? 1 : (double)Interlocked.Read(ref _position) / _sound.Samples.Length;

    public void Stop() => _stopped = true;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_stopped)
        {
            return 0;
        }

        var samples = _sound.Samples;
        long position = _position;
        int n = (int)Math.Min(samples.Length - position, count);
        float gain = _gain();
        for (int i = 0; i < n; i++)
        {
            buffer[offset + i] = samples[position + i] * gain;
        }
        Interlocked.Exchange(ref _position, position + n);
        return n;
    }
}
