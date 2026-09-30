using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundboard.Core.Audio;

/// <summary>
/// Captures the real microphone via WASAPI and exposes it as a 48 kHz stereo float stream.
/// </summary>
/// <remarks>
/// The mic and the virtual cable run on different hardware clocks, so the buffer between
/// them slowly fills or drains. Underruns are padded with silence (ReadFully); if the buffer
/// grows past <see cref="MaxBufferedMs"/> we drop audio back down to <see cref="TargetBufferedMs"/>.
/// That occasional trim is inaudible in speech and keeps latency bounded.
/// </remarks>
internal sealed class MicInput : IDisposable
{
    private const int MaxBufferedMs = 60;
    private const int TargetBufferedMs = 20;

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly WaveFormat _captureFormat;
    private readonly bool _captureIsFloat;
    private byte[] _converted = [];
    private byte[] _discard = [];

    public MicInput(string deviceId, int bufferMs = 20)
    {
        _device = AudioDevices.Open(deviceId);
        _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: bufferMs);
        _captureFormat = _capture.WaveFormat;
        _captureIsFloat = IsFloat(_captureFormat);

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(_captureFormat.SampleRate, 2))
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };

        ISampleProvider output = _buffer.ToSampleProvider();
        if (_captureFormat.SampleRate != AudioFormat.SampleRate)
        {
            output = new WdlResamplingSampleProvider(output, AudioFormat.SampleRate);
        }
        Output = output;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    public ISampleProvider Output { get; }

    /// <summary>Raised when capture stops; the exception is non-null if a device error caused it.</summary>
    public event Action<Exception?>? Stopped;

    public void Start() => _capture.StartRecording();

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception)
        {
            // Device may already be gone.
        }
        _capture.Dispose();
        _device.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int channels = _captureFormat.Channels;
        int bytesPerSample = _captureFormat.BitsPerSample / 8;
        int frames = e.BytesRecorded / (bytesPerSample * channels);
        int outBytes = frames * 2 * sizeof(float);
        if (_converted.Length < outBytes)
        {
            _converted = new byte[outBytes];
        }

        var output = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(_converted.AsSpan(0, outBytes));
        var input = e.Buffer.AsSpan(0, e.BytesRecorded);
        for (int f = 0; f < frames; f++)
        {
            int index = f * channels * bytesPerSample;
            float left = ReadSample(input, index, bytesPerSample, _captureIsFloat);
            float right = channels > 1 ? ReadSample(input, index + bytesPerSample, bytesPerSample, _captureIsFloat) : left;
            output[f * 2] = left;
            output[f * 2 + 1] = right;
        }

        TrimDrift();
        _buffer.AddSamples(_converted, 0, outBytes);
    }

    private void TrimDrift()
    {
        if (_buffer.BufferedDuration.TotalMilliseconds <= MaxBufferedMs)
        {
            return;
        }
        int excess = _buffer.BufferedBytes - _buffer.WaveFormat.ConvertLatencyToByteSize(TargetBufferedMs);
        excess -= excess % _buffer.WaveFormat.BlockAlign;
        if (excess <= 0)
        {
            return;
        }
        if (_discard.Length < excess)
        {
            _discard = new byte[excess];
        }
        _buffer.Read(_discard, 0, excess);
    }

    internal static float ReadSample(ReadOnlySpan<byte> data, int index, int bytesPerSample, bool isFloat) =>
        bytesPerSample switch
        {
            2 => BitConverter.ToInt16(data.Slice(index, 2)) / 32768f,
            3 => ((data[index] | (data[index + 1] << 8) | ((sbyte)data[index + 2] << 16))) / 8388608f,
            4 when isFloat => BitConverter.ToSingle(data.Slice(index, 4)),
            4 => BitConverter.ToInt32(data.Slice(index, 4)) / 2147483648f,
            _ => 0f,
        };

    private static bool IsFloat(WaveFormat format) => format.Encoding switch
    {
        WaveFormatEncoding.IeeeFloat => true,
        WaveFormatEncoding.Extensible when format is WaveFormatExtensible ext => ext.SubFormat == IeeeFloatSubFormat,
        // Windows shared-mode mix formats are almost always 32-bit float.
        WaveFormatEncoding.Extensible => format.BitsPerSample == 32,
        _ => false,
    };
}
