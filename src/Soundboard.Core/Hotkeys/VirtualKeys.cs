namespace Soundboard.Core.Hotkeys;

public static class VirtualKeys
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int Escape = 0x1B;
    public const int LeftWin = 0x5B;
    public const int RightWin = 0x5C;
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftControl = 0xA2;
    public const int RightControl = 0xA3;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;

    /// <summary>
    /// The low-level hook reports left/right modifiers separately; treat them as one key
    /// so "Ctrl + F1" works with either Ctrl.
    /// </summary>
    public static int Normalize(int vk) => vk switch
    {
        LeftShift or RightShift => Shift,
        LeftControl or RightControl => Control,
        LeftAlt or RightAlt => Alt,
        RightWin => LeftWin,
        _ => vk,
    };

    public static bool IsModifier(int vk) => vk is Shift or Control or Alt or LeftWin;
}
