using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Soundboard.Core.Audio;

public sealed record AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> GetInputs() => Enumerate(DataFlow.Capture);

    public static IReadOnlyList<AudioDeviceInfo> GetOutputs() => Enumerate(DataFlow.Render);

    public static string? GetDefaultId(DataFlow flow, Role role = Role.Communications)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch (COMException)
        {
            // No default device of this kind.
            return null;
        }
    }

    /// <summary>Finds the VB-Audio "CABLE Input" playback device, if installed.</summary>
    public static AudioDeviceInfo? FindVirtualCable(IEnumerable<AudioDeviceInfo> outputs) =>
        outputs.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

    internal static MMDevice Open(string id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    private static List<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }
        }
        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
