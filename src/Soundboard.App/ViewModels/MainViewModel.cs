using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Soundboard.Core.Audio;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Settings;

namespace Soundboard.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Placeholder entry meaning "don't use a device" (no mic passthrough / no monitor).</summary>
    public static readonly AudioDeviceInfo NoDevice = new("", "(none)");

    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly AudioEngine _engine = new();
    private readonly HotkeyProcessor _hotkeys = new(KeyboardHook.IsKeyPhysicallyDown);
    private readonly KeyboardHook _hook;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _playbackTimer;
    private bool _initializing = true;

    public MainViewModel()
    {
        _settings = _store.Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => SaveNow();

        SoundsView = CollectionViewSource.GetDefaultView(Sounds);
        SoundsView.Filter = FilterSound;
        Sounds.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SoundCountText));

        // Drives the playing highlight and progress bars (~30 fps; only touches sounds that are playing).
        _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnPlaybackTick,
            Dispatcher.CurrentDispatcher);

        _micVolume = _settings.MicVolume;
        _soundsVolume = _settings.SoundsVolume;
        _monitorVolume = _settings.MonitorVolume;
        _monitorEnabled = _settings.MonitorEnabled;
        _suppressHotkeys = _settings.SuppressHotkeys;
        _stopAllHotkey = _settings.StopAllHotkey;
        ApplyLevelsToEngine();

        LoadDevices();

        foreach (var entry in _settings.Sounds)
        {
            AddItem(new SoundItemViewModel(entry));
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        _engine.DeviceError += ex => dispatcher.BeginInvoke(() =>
        {
            _engine.Stop();
            IsRunning = false;
            Status = $"Audio device error: {ex.Message}";
        });

        _hotkeys.SuppressMatchedKeys = _suppressHotkeys;
        _hotkeys.Triggered += OnHotkeyTriggered;
        RebuildBindings();
        _hook = new KeyboardHook(_hotkeys);
        _hook.Install();

        _initializing = false;
        if (SelectedCable is not null)
        {
            StartEngine();
        }
        else
        {
            Status = "Select the virtual cable output (e.g. \"CABLE Input\") and press Start.";
        }
    }

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> MonitorDevices { get; } = [];
    public ObservableCollection<SoundItemViewModel> Sounds { get; } = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMic;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedCable;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMonitor;

    [ObservableProperty]
    private double _micVolume;

    [ObservableProperty]
    private double _soundsVolume;

    [ObservableProperty]
    private double _monitorVolume;

    [ObservableProperty]
    private bool _monitorEnabled;

    [ObservableProperty]
    private bool _suppressHotkeys;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopAllHotkeyText), nameof(HasStopAllHotkey))]
    private Hotkey? _stopAllHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopAllHotkeyText))]
    private bool _isRecordingStopAll;

    public string StartStopText => IsRunning ? "Stop" : "Start";

    public static string VersionText { get; } =
        "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?");

    public string StopAllHotkeyText =>
        IsRecordingStopAll ? "Press keys…"
        : StopAllHotkey is null ? "Set hotkey"
        : KeyNames.Format(StopAllHotkey);

    public bool HasStopAllHotkey => StopAllHotkey is not null;

    /// <summary>Sounds filtered by <see cref="SearchText"/>; this is what the card grid shows.</summary>
    public ICollectionView SoundsView { get; }

    public string SoundCountText => Sounds.Count == 1 ? "1 sound" : $"{Sounds.Count} sounds";

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => SoundsView.Refresh();

    [RelayCommand]
    private void PlayOrStop(SoundItemViewModel item)
    {
        if (item.HasActivePlayback)
        {
            item.StopPlayback();
        }
        else
        {
            Trigger(item);
        }
    }

    [RelayCommand]
    private void CycleMode(SoundItemViewModel item) => item.CycleMode();

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        foreach (var item in Sounds)
        {
            if (item.IsPlaying || item.HasActivePlayback)
            {
                item.UpdatePlaybackState();
            }
        }
    }

    private bool FilterSound(object obj) =>
        string.IsNullOrWhiteSpace(SearchText)
        || (obj is SoundItemViewModel item
            && (item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || item.HotkeyText.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

    public void AddFiles(IEnumerable<string> paths)
    {
        int skipped = 0;
        foreach (var path in paths)
        {
            if (!CachedSound.IsSupported(path))
            {
                skipped++;
                continue;
            }
            var entry = new SoundEntry
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Path = path,
            };
            _settings.Sounds.Add(entry);
            AddItem(new SoundItemViewModel(entry));
        }
        if (skipped > 0)
        {
            Status = $"Skipped {skipped} unsupported file(s).";
        }
        ScheduleSave();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex)
        {
            Status = $"Could not save settings: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _playbackTimer.Stop();
        _hotkeys.CancelRecording();
        _hook.Dispose();
        _engine.Dispose();
        SaveNow();
    }

    [RelayCommand]
    private void ToggleEngine()
    {
        if (IsRunning)
        {
            _engine.Stop();
            IsRunning = false;
            Status = "Stopped.";
        }
        else
        {
            StartEngine();
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        _initializing = true;
        LoadDevices();
        _initializing = false;
        RestartIfRunning();
    }

    [RelayCommand]
    private void AddSounds()
    {
        var patterns = string.Join(";", CachedSound.SupportedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = "Add sounds",
            Multiselect = true,
            Filter = $"Audio files|{patterns}|All files|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    [RelayCommand]
    private void RemoveSound(SoundItemViewModel item)
    {
        item.StopPlayback();
        item.PropertyChanged -= OnItemPropertyChanged;
        Sounds.Remove(item);
        _settings.Sounds.Remove(item.Entry);
        RebuildBindings();
        ScheduleSave();
    }

    [RelayCommand]
    private void PlaySound(SoundItemViewModel item) => Trigger(item);

    [RelayCommand]
    private void StopAll()
    {
        _engine.StopAll();
        foreach (var item in Sounds)
        {
            item.StopPlayback();
        }
    }

    [RelayCommand]
    private void RecordHotkey(SoundItemViewModel item)
    {
        _hotkeys.CancelRecording();
        item.IsRecording = true;
        _hotkeys.BeginRecording(hotkey =>
        {
            item.IsRecording = false;
            if (hotkey is { } hk)
            {
                ReleaseHotkey(hk);
                item.Hotkey = hk;
                RebuildBindings();
                ScheduleSave();
            }
        });
    }

    [RelayCommand]
    private void ClearHotkey(SoundItemViewModel item)
    {
        item.Hotkey = null;
        RebuildBindings();
        ScheduleSave();
    }

    [RelayCommand]
    private void RecordStopAllHotkey()
    {
        _hotkeys.CancelRecording();
        IsRecordingStopAll = true;
        _hotkeys.BeginRecording(hotkey =>
        {
            IsRecordingStopAll = false;
            if (hotkey is { } hk)
            {
                ReleaseHotkey(hk);
                StopAllHotkey = hk;
            }
        });
    }

    [RelayCommand]
    private void ClearStopAllHotkey() => StopAllHotkey = null;

    // In settings, "" means the user explicitly chose "(none)"; null means "use the system default".
    partial void OnSelectedMicChanged(AudioDeviceInfo? value) => OnDeviceSelectionChanged(() => _settings.MicDeviceId = value?.Id);

    partial void OnSelectedCableChanged(AudioDeviceInfo? value) => OnDeviceSelectionChanged(() => _settings.CableDeviceId = value?.Id);

    partial void OnSelectedMonitorChanged(AudioDeviceInfo? value) => OnDeviceSelectionChanged(() => _settings.MonitorDeviceId = value?.Id);

    partial void OnMicVolumeChanged(double value) => OnLevelChanged();

    partial void OnSoundsVolumeChanged(double value) => OnLevelChanged();

    partial void OnMonitorVolumeChanged(double value) => OnLevelChanged();

    partial void OnMonitorEnabledChanged(bool value) => OnLevelChanged();

    partial void OnSuppressHotkeysChanged(bool value)
    {
        _settings.SuppressHotkeys = value;
        _hotkeys.SuppressMatchedKeys = value;
        ScheduleSave();
    }

    partial void OnStopAllHotkeyChanged(Hotkey? value)
    {
        _settings.StopAllHotkey = value;
        RebuildBindings();
        ScheduleSave();
    }

    private void StartEngine()
    {
        if (SelectedCable is null || SelectedCable == NoDevice)
        {
            Status = "Select the virtual cable output first.";
            return;
        }
        if (IsVirtualCable(SelectedMic))
        {
            Status = "The microphone is set to the virtual cable itself; that would loop audio. Pick your real mic.";
            return;
        }

        var monitorId = IdOf(SelectedMonitor);
        if (monitorId == SelectedCable.Id)
        {
            monitorId = null;
        }

        try
        {
            _engine.Start(new EngineDevices(SelectedCable.Id, IdOf(SelectedMic), monitorId));
            IsRunning = true;
            Status = $"Running → {SelectedCable.Name}. In Discord/games, choose \"CABLE Output\" as your microphone.";
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Status = $"Could not start audio: {ex.Message}";
        }
    }

    private void RestartIfRunning()
    {
        if (IsRunning)
        {
            StartEngine();
        }
    }

    private void LoadDevices()
    {
        var inputs = AudioDevices.GetInputs();
        var outputs = AudioDevices.GetOutputs();

        InputDevices.Clear();
        InputDevices.Add(NoDevice);
        foreach (var d in inputs) InputDevices.Add(d);

        OutputDevices.Clear();
        foreach (var d in outputs) OutputDevices.Add(d);

        MonitorDevices.Clear();
        MonitorDevices.Add(NoDevice);
        foreach (var d in outputs) MonitorDevices.Add(d);

        // Restore saved choices, falling back to sensible defaults on first run.
        var micId = _settings.MicDeviceId ?? AudioDevices.GetDefaultId(DataFlow.Capture);
        SelectedMic = inputs.FirstOrDefault(d => d.Id == micId && !IsVirtualCable(d))
            ?? (_settings.MicDeviceId == "" ? NoDevice : null);

        SelectedCable = outputs.FirstOrDefault(d => d.Id == _settings.CableDeviceId)
            ?? AudioDevices.FindVirtualCable(outputs);

        var monitorId = _settings.MonitorDeviceId ?? AudioDevices.GetDefaultId(DataFlow.Render, Role.Multimedia);
        SelectedMonitor = outputs.FirstOrDefault(d => d.Id == monitorId && !IsVirtualCable(d))
            ?? (_settings.MonitorDeviceId == "" ? NoDevice : null);
    }

    private void AddItem(SoundItemViewModel item)
    {
        item.PropertyChanged += OnItemPropertyChanged;
        Sounds.Add(item);
        _ = LoadSoundAsync(item);
    }

    private static async Task LoadSoundAsync(SoundItemViewModel item)
    {
        try
        {
            // Decode off the UI thread: the keyboard hook runs on the UI thread and must never be starved.
            item.Sound = await Task.Run(() => CachedSound.Load(item.Path));
            item.LoadError = null;
        }
        catch (Exception ex)
        {
            item.LoadError = ex is System.IO.FileNotFoundException ? "File not found" : ex.Message;
        }
    }

    private void OnHotkeyTriggered(Hotkey hotkey)
    {
        if (hotkey == StopAllHotkey)
        {
            StopAll();
            return;
        }
        foreach (var item in Sounds)
        {
            if (item.Hotkey == hotkey)
            {
                Trigger(item);
            }
        }
    }

    private void Trigger(SoundItemViewModel item)
    {
        if (item.Sound is not { } sound)
        {
            return;
        }
        if (!IsRunning)
        {
            Status = "Audio engine is stopped. Press Start.";
            return;
        }

        switch (item.Mode)
        {
            case PlaybackMode.Toggle when item.HasActivePlayback:
                item.StopPlayback();
                return;
            case PlaybackMode.Restart:
            case PlaybackMode.Toggle:
                item.StopPlayback();
                break;
        }

        if (_engine.Play(sound, () => item.Gain) is { } playback)
        {
            item.AddPlayback(playback);
        }
    }

    /// <summary>A hotkey can only belong to one action; unbind it from wherever it was.</summary>
    private void ReleaseHotkey(Hotkey hotkey)
    {
        foreach (var other in Sounds.Where(s => s.Hotkey == hotkey))
        {
            other.Hotkey = null;
        }
        if (StopAllHotkey == hotkey)
        {
            StopAllHotkey = null;
        }
    }

    private void RebuildBindings()
    {
        var bindings = Sounds.Select(s => s.Hotkey).Append(StopAllHotkey).OfType<Hotkey>();
        _hotkeys.SetBindings(bindings);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SoundItemViewModel.Name) or nameof(SoundItemViewModel.Volume)
            or nameof(SoundItemViewModel.Mode) or nameof(SoundItemViewModel.Hotkey))
        {
            ScheduleSave();
        }
    }

    private void OnDeviceSelectionChanged(Action updateSettings)
    {
        if (_initializing)
        {
            return;
        }
        updateSettings();
        ScheduleSave();
        RestartIfRunning();
    }

    private void OnLevelChanged()
    {
        _settings.MicVolume = (float)MicVolume;
        _settings.SoundsVolume = (float)SoundsVolume;
        _settings.MonitorVolume = (float)MonitorVolume;
        _settings.MonitorEnabled = MonitorEnabled;
        ApplyLevelsToEngine();
        ScheduleSave();
    }

    private void ApplyLevelsToEngine()
    {
        _engine.MicVolume = (float)MicVolume;
        _engine.SoundsVolume = (float)SoundsVolume;
        _engine.MonitorVolume = (float)MonitorVolume;
        _engine.MonitorEnabled = MonitorEnabled;
    }

    private void ScheduleSave()
    {
        if (_initializing)
        {
            return;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static string? IdOf(AudioDeviceInfo? device) =>
        device is null || device == NoDevice ? null : device.Id;

    private static bool IsVirtualCable(AudioDeviceInfo? device) =>
        device is not null && device.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
        && device.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);
}
