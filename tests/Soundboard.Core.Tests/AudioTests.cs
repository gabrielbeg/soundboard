using System.Text.Json;
using NAudio.Wave;
using Soundboard.Core.Audio;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Settings;

namespace Soundboard.Core.Tests;

public class AudioTests
{
    [Fact]
    public void ToStereo_DuplicatesMono()
    {
        var source = new ArraySampleProvider([0.1f, 0.2f, 0.3f], channels: 1);
        var stereo = new ToStereoSampleProvider(source);
        var buffer = new float[6];

        int read = stereo.Read(buffer, 0, buffer.Length);

        Assert.Equal(6, read);
        Assert.Equal([0.1f, 0.1f, 0.2f, 0.2f, 0.3f, 0.3f], buffer);
    }

    [Fact]
    public void ToStereo_KeepsFrontPairOfMultichannel()
    {
        var source = new ArraySampleProvider([1f, 2f, 3f, 4f, 5f, 6f], channels: 3);
        var stereo = new ToStereoSampleProvider(source);
        var buffer = new float[4];

        stereo.Read(buffer, 0, buffer.Length);

        Assert.Equal([1f, 2f, 4f, 5f], buffer);
    }

    [Fact]
    public void Voice_AppliesGainAndEnds()
    {
        var voice = new SoundVoice(new CachedSound([0.5f, 0.5f, 1f, 1f]), () => 0.5f);
        var buffer = new float[8];

        int read = voice.Read(buffer, 0, buffer.Length);

        Assert.Equal(4, read);
        Assert.Equal([0.25f, 0.25f, 0.5f, 0.5f], buffer[..4]);
        Assert.True(voice.IsFinished);
    }

    [Fact]
    public void Voice_StopsImmediately()
    {
        var voice = new SoundVoice(new CachedSound(new float[100]), () => 1f);
        voice.Stop();

        Assert.Equal(0, voice.Read(new float[10], 0, 10));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-0.8f)]
    public void Limiter_IsTransparentBelowThreshold(float x) =>
        Assert.Equal(x, SoftLimiterSampleProvider.Limit(x));

    [Theory]
    [InlineData(1.5f)]
    [InlineData(-4f)]
    public void Limiter_KeepsPeaksWithinRange(float x)
    {
        float y = SoftLimiterSampleProvider.Limit(x);
        Assert.InRange(Math.Abs(y), 0.9f, 1f);
        Assert.Equal(Math.Sign(x), Math.Sign(y));
    }

    [Fact]
    public void MicSample_Decodes16And24Bit()
    {
        byte[] int16 = BitConverter.GetBytes((short)-16384);
        byte[] int24 = [0x00, 0x00, 0x40]; // +0.5

        Assert.Equal(-0.5f, MicInput.ReadSample(int16, 0, 2, isFloat: false));
        Assert.Equal(0.5f, MicInput.ReadSample(int24, 0, 3, isFloat: false));
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"soundboard-test-{Guid.NewGuid()}.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings
            {
                StopAllHotkey = new Hotkey(VirtualKeys.Control, 0x70),
                Sounds = [new SoundEntry { Name = "Airhorn", Path = @"C:\a.wav", Mode = PlaybackMode.Toggle, Hotkey = new Hotkey(0x61) }],
            };

            store.Save(settings);
            var loaded = store.Load();

            Assert.Equal(settings.StopAllHotkey, loaded.StopAllHotkey);
            var sound = Assert.Single(loaded.Sounds);
            Assert.Equal("Airhorn", sound.Name);
            Assert.Equal(PlaybackMode.Toggle, sound.Mode);
            Assert.Equal(new Hotkey(0x61), sound.Hotkey);
            Assert.Contains("\"Toggle\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class ArraySampleProvider(float[] samples, int channels) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
