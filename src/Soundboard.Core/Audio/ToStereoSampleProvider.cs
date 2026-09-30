using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>
/// Converts any channel count to stereo: mono is duplicated, stereo passes through,
/// and for more than two channels the front left/right pair is kept.
/// </summary>
public sealed class ToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _inChannels;
    private float[] _sourceBuffer = [];

    public ToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _inChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_inChannels == 2)
        {
            return _source.Read(buffer, offset, count);
        }

        int frames = count / 2;
        int needed = frames * _inChannels;
        if (_sourceBuffer.Length < needed)
        {
            _sourceBuffer = new float[needed];
        }

        int read = _source.Read(_sourceBuffer, 0, needed);
        int framesRead = read / _inChannels;
        for (int f = 0; f < framesRead; f++)
        {
            int i = f * _inChannels;
            float left = _sourceBuffer[i];
            float right = _inChannels == 1 ? left : _sourceBuffer[i + 1];
            buffer[offset + f * 2] = left;
            buffer[offset + f * 2 + 1] = right;
        }
        return framesRead * 2;
    }
}
