using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundboard.Core.Audio;

public sealed record EngineDevices(string CableOutputId, string? MicInputId, string? MonitorOutputId);

/// <summary>
/// Routes audio:
/// <code>
///   real mic ─► micVolume ─┐
///                          ├─► cable mixer ─► limiter ─► virtual cable ("CABLE Input")
///   sounds ─► soundsVolume ┘
///   sounds ─► monitorVolume ─► limiter ─► headphones
/// </code>
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int CableLatencyMs = 30;
    private const int MonitorLatencyMs = 50;

    private readonly object _gate = new();
    private readonly List<Playback> _active = [];

    private MicInput? _mic;
    private MMDevice? _cableDevice;
    private MMDevice? _monitorDevice;
    private WasapiOut? _cableOut;
    private WasapiOut? _monitorOut;
    private MixingSampleProvider? _soundsToCable;
    private MixingSampleProvider? _soundsToMonitor;
    private VolumeSampleProvider? _micVolumeStage;
    private VolumeSampleProvider? _soundsVolumeStage;
    private VolumeSampleProvider? _monitorVolumeStage;

    private float _micVolume = 1f;
    private float _soundsVolume = 1f;
    private float _monitorVolume = 0.8f;
    private bool _monitorEnabled = true;

    public bool IsRunning { get; private set; }

    /// <summary>Raised (on the thread that started the engine) when a device fails mid-stream.</summary>
    public event Action<Exception>? DeviceError;

    public float MicVolume
    {
        get => _micVolume;
        set
        {
            _micVolume = value;
            if (_micVolumeStage is { } stage) stage.Volume = value;
        }
    }

    public float SoundsVolume
    {
        get => _soundsVolume;
        set
        {
            _soundsVolume = value;
            if (_soundsVolumeStage is { } stage) stage.Volume = value;
        }
    }

    public float MonitorVolume
    {
        get => _monitorVolume;
        set
        {
            _monitorVolume = value;
            ApplyMonitorVolume();
        }
    }

    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            _monitorEnabled = value;
            ApplyMonitorVolume();
        }
    }

    public void Start(EngineDevices devices)
    {
        Stop();
        try
        {
            StartCore(devices);
            IsRunning = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        StopAll();
        IsRunning = false;

        DisposeOutput(ref _cableOut);
        DisposeOutput(ref _monitorOut);
        _mic?.Dispose();
        _mic = null;
        _cableDevice?.Dispose();
        _cableDevice = null;
        _monitorDevice?.Dispose();
        _monitorDevice = null;

        _soundsToCable = null;
        _soundsToMonitor = null;
        _micVolumeStage = null;
        _soundsVolumeStage = null;
        _monitorVolumeStage = null;
    }

    /// <summary>Starts a sound. Returns null if the engine isn't running.</summary>
    public Playback? Play(CachedSound sound, Func<float> gain)
    {
        var toCable = _soundsToCable;
        if (!IsRunning || toCable is null)
        {
            return null;
        }

        var voices = new List<SoundVoice>(2);
        var cableVoice = new SoundVoice(sound, gain);
        voices.Add(cableVoice);

        SoundVoice? monitorVoice = null;
        if (_soundsToMonitor is not null)
        {
            monitorVoice = new SoundVoice(sound, gain);
            voices.Add(monitorVoice);
        }

        var playback = new Playback(voices);
        lock (_gate)
        {
            _active.RemoveAll(p => p.IsFinished);
            _active.Add(playback);
        }

        // Add both voices back to back so they start within the same buffer period.
        toCable.AddMixerInput(cableVoice);
        if (monitorVoice is not null)
        {
            _soundsToMonitor!.AddMixerInput(monitorVoice);
        }
        return playback;
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var playback in _active)
            {
                playback.Stop();
            }
            _active.Clear();
        }
    }

    public void Dispose() => Stop();

    private void StartCore(EngineDevices devices)
    {
        // Virtual cable: real mic + sounds.
        var cableMixer = new MixingSampleProvider(AudioFormat.Mix) { ReadFully = true };

        if (devices.MicInputId is { } micId)
        {
            _mic = new MicInput(micId);
            _mic.Stopped += OnMicStopped;
            _micVolumeStage = new VolumeSampleProvider(_mic.Output) { Volume = _micVolume };
            cableMixer.AddMixerInput(_micVolumeStage);
        }

        _soundsToCable = new MixingSampleProvider(AudioFormat.Mix) { ReadFully = true };
        _soundsVolumeStage = new VolumeSampleProvider(_soundsToCable) { Volume = _soundsVolume };
        cableMixer.AddMixerInput(_soundsVolumeStage);

        _cableDevice = AudioDevices.Open(devices.CableOutputId);
        _cableOut = CreateOutput(_cableDevice, CableLatencyMs, new SoftLimiterSampleProvider(cableMixer));

        // Monitor: sounds only, so you don't hear your own voice delayed.
        if (devices.MonitorOutputId is { } monitorId)
        {
            _soundsToMonitor = new MixingSampleProvider(AudioFormat.Mix) { ReadFully = true };
            _monitorVolumeStage = new VolumeSampleProvider(_soundsToMonitor);
            ApplyMonitorVolume();
            _monitorDevice = AudioDevices.Open(monitorId);
            _monitorOut = CreateOutput(_monitorDevice, MonitorLatencyMs, new SoftLimiterSampleProvider(_monitorVolumeStage));
        }

        _mic?.Start();
        _cableOut.Play();
        _monitorOut?.Play();
    }

    private WasapiOut CreateOutput(MMDevice device, int latencyMs, ISampleProvider source)
    {
        var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latencyMs);
        output.Init(source);
        output.PlaybackStopped += OnOutputStopped;
        return output;
    }

    private void DisposeOutput(ref WasapiOut? output)
    {
        if (output is null)
        {
            return;
        }
        output.PlaybackStopped -= OnOutputStopped;
        try
        {
            output.Stop();
        }
        catch (Exception)
        {
            // Device may already be gone.
        }
        output.Dispose();
        output = null;
    }

    private void ApplyMonitorVolume()
    {
        if (_monitorVolumeStage is { } stage)
        {
            stage.Volume = _monitorEnabled ? _monitorVolume : 0f;
        }
    }

    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            DeviceError?.Invoke(e.Exception);
        }
    }

    private void OnMicStopped(Exception? exception)
    {
        if (exception is not null)
        {
            DeviceError?.Invoke(exception);
        }
    }
}
