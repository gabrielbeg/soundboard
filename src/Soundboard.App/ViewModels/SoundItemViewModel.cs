using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.Core.Audio;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Settings;

namespace Soundboard.App.ViewModels;

public partial class SoundItemViewModel : ObservableObject
{
    private readonly List<Playback> _playbacks = [];

    // Read from the audio thread on every buffer, so it's a plain volatile field rather than a bound property.
    private volatile float _gain;

    public SoundItemViewModel(SoundEntry entry)
    {
        Entry = entry;
        _name = entry.Name;
        _volume = entry.Volume;
        _mode = entry.Mode;
        _hotkey = entry.Hotkey;
        _gain = entry.Volume;
    }

    public SoundEntry Entry { get; }

    public string Path => Entry.Path;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText), nameof(ModeGlyph), nameof(ModeDescription))]
    private PlaybackMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText), nameof(HasHotkey))]
    private Hotkey? _hotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyText))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private CachedSound? _sound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(HasError))]
    private string? _loadError;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _progress;

    public string HotkeyText =>
        IsRecording ? "Press keys…"
        : Hotkey is null ? "Set hotkey"
        : KeyNames.Format(Hotkey);

    public bool HasHotkey => Hotkey is not null;

    public bool HasError => LoadError is not null;

    public string DurationText =>
        LoadError is not null ? LoadError
        : Sound is null ? "Loading…"
        : Sound.Duration.ToString(@"m\:ss\.f");

    public string VolumeText => $"{Volume:P0}";

    public string ModeText => Mode.ToString();

    public string ModeGlyph => Mode switch
    {
        PlaybackMode.Overlap => "",
        PlaybackMode.Restart => "",
        _ => "",
    };

    public string ModeDescription => Mode switch
    {
        PlaybackMode.Overlap => "Overlap: each press plays another copy on top. Click to change.",
        PlaybackMode.Restart => "Restart: each press starts the sound over. Click to change.",
        _ => "Toggle: press to play, press again to stop. Click to change.",
    };

    public float Gain => _gain;

    /// <summary>True if any playback of this sound is still running (prunes finished ones).</summary>
    public bool HasActivePlayback
    {
        get
        {
            _playbacks.RemoveAll(p => p.IsFinished);
            return _playbacks.Count > 0;
        }
    }

    public void AddPlayback(Playback playback)
    {
        _playbacks.RemoveAll(p => p.IsFinished);
        _playbacks.Add(playback);
        UpdatePlaybackState();
    }

    public void StopPlayback()
    {
        foreach (var playback in _playbacks)
        {
            playback.Stop();
        }
        _playbacks.Clear();
        UpdatePlaybackState();
    }

    /// <summary>Refreshes <see cref="IsPlaying"/> and <see cref="Progress"/>; called from a UI timer.</summary>
    public void UpdatePlaybackState()
    {
        IsPlaying = HasActivePlayback;
        Progress = IsPlaying ? _playbacks[^1].Progress : 0;
    }

    public void CycleMode() => Mode = Mode switch
    {
        PlaybackMode.Overlap => PlaybackMode.Restart,
        PlaybackMode.Restart => PlaybackMode.Toggle,
        _ => PlaybackMode.Overlap,
    };

    partial void OnNameChanged(string value) => Entry.Name = value;

    partial void OnVolumeChanged(double value)
    {
        Entry.Volume = (float)value;
        _gain = (float)value;
    }

    partial void OnModeChanged(PlaybackMode value) => Entry.Mode = value;

    partial void OnHotkeyChanged(Hotkey? value) => Entry.Hotkey = value;
}
