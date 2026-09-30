using System.Text.Json.Serialization;

namespace Soundboard.Core.Hotkeys;

/// <summary>
/// A combination of one or two Windows virtual-key codes. Order-insensitive:
/// keys are normalized so modifiers come first, then by key code.
/// </summary>
public readonly record struct Hotkey
{
    [JsonConstructor]
    public Hotkey(int key1, int key2 = 0)
    {
        if (key2 != 0 && SortKey(key2) < SortKey(key1))
        {
            (key1, key2) = (key2, key1);
        }
        Key1 = key1;
        Key2 = key2;
    }

    public int Key1 { get; }

    /// <summary>Second key, or 0 for a single-key hotkey.</summary>
    public int Key2 { get; }

    [JsonIgnore]
    public IEnumerable<int> Keys => Key2 == 0 ? [Key1] : [Key1, Key2];

    /// <summary>Builds a hotkey from 1–2 distinct keys; returns null for any other count.</summary>
    public static Hotkey? From(IEnumerable<int> keys)
    {
        var distinct = keys.Distinct().Take(3).ToArray();
        return distinct.Length switch
        {
            1 => new Hotkey(distinct[0]),
            2 => new Hotkey(distinct[0], distinct[1]),
            _ => null,
        };
    }

    public override string ToString() => string.Join(" + ", Keys.Select(k => $"0x{k:X2}"));

    private static int SortKey(int vk) => (VirtualKeys.IsModifier(vk) ? 0 : 1000) + vk;
}
