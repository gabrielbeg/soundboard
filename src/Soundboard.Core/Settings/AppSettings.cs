using Soundboard.Core.Hotkeys;

namespace Soundboard.Core.Settings;

public enum PlaybackMode
{
    /// <summary>Each press starts another copy on top of any already playing.</summary>
    Overlap,

    /// <summary>Each press stops the sound and starts it from the beginning.</summary>
    Restart,

    /// <summary>Press to play, press again to stop.</summary>
    Toggle,
}

public sealed class SoundEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public float Volume { get; set; } = 1f;
    public PlaybackMode Mode { get; set; } = PlaybackMode.Restart;
    public Hotkey? Hotkey { get; set; }
}

public sealed class AppSettings
{
    public string? MicDeviceId { get; set; }
    public string? CableDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }

    public float MicVolume { get; set; } = 1f;
    public float SoundsVolume { get; set; } = 1f;
    public float MonitorVolume { get; set; } = 0.8f;
    public bool MonitorEnabled { get; set; } = true;

    public bool SuppressHotkeys { get; set; }
    public Hotkey? StopAllHotkey { get; set; }

    public List<SoundEntry> Sounds { get; set; } = [];
}
