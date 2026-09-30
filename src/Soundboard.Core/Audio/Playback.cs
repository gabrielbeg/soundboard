namespace Soundboard.Core.Audio;

/// <summary>
/// Handle for one triggered sound. The same sound is sent to the virtual cable and
/// (optionally) the monitor as two independent voices, so each output pulls at its own pace.
/// </summary>
public sealed class Playback
{
    private readonly IReadOnlyList<SoundVoice> _voices;

    internal Playback(IReadOnlyList<SoundVoice> voices)
    {
        _voices = voices;
    }

    public bool IsFinished => _voices.All(v => v.IsFinished);

    /// <summary>Fraction played (0–1), taken from the voice feeding the virtual cable.</summary>
    public double Progress => _voices[0].Progress;

    public void Stop()
    {
        foreach (var voice in _voices)
        {
            voice.Stop();
        }
    }
}
