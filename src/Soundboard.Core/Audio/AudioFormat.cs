using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>
/// The single internal format everything is converted to before mixing.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    public static WaveFormat Mix { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
}
