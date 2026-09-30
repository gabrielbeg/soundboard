using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundboard.Core.Audio;

/// <summary>
/// A sound fully decoded into memory in <see cref="AudioFormat.Mix"/>, so a hotkey can start it instantly.
/// </summary>
public sealed class CachedSound
{
    public static readonly string[] SupportedExtensions = [".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif"];

    public CachedSound(float[] samples)
    {
        Samples = samples;
    }

    /// <summary>Interleaved stereo float samples at 48 kHz.</summary>
    public float[] Samples { get; }

    public TimeSpan Duration =>
        TimeSpan.FromSeconds((double)Samples.Length / (AudioFormat.SampleRate * AudioFormat.Channels));

    public static CachedSound Load(string path)
    {
        using var reader = OpenReader(path);
        ISampleProvider provider = (ISampleProvider)reader;

        if (provider.WaveFormat.Channels != AudioFormat.Channels)
        {
            provider = new ToStereoSampleProvider(provider);
        }
        if (provider.WaveFormat.SampleRate != AudioFormat.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, AudioFormat.SampleRate);
        }

        return new CachedSound(ReadAll(provider));
    }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static IDisposable OpenReader(string path) =>
        Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            // AudioFileReader handles wav/mp3/aiff itself and falls back to Media Foundation for the rest.
            : new AudioFileReader(path);

    private static float[] ReadAll(ISampleProvider provider)
    {
        var all = new List<float>();
        var buffer = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, read));
        }
        return all.ToArray();
    }
}
