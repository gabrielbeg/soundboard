using System.Windows.Input;
using Soundboard.Core.Hotkeys;

namespace Soundboard.App;

/// <summary>Human-readable names for virtual-key codes, e.g. "Ctrl + Num 1".</summary>
public static class KeyNames
{
    public static string Format(Hotkey? hotkey) =>
        hotkey is { } h ? string.Join(" + ", h.Keys.Select(Name)) : "None";

    public static string Name(int vk)
    {
        switch (vk)
        {
            case VirtualKeys.Shift: return "Shift";
            case VirtualKeys.Control: return "Ctrl";
            case VirtualKeys.Alt: return "Alt";
            case VirtualKeys.LeftWin: return "Win";
        }

        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Divide => "Num /",
            Key.Decimal => "Num .",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Capital => "Caps Lock",
            Key.Next => "Page Down",
            Key.Prior => "Page Up",
            Key.Snapshot => "Print Screen",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.None => $"Key 0x{vk:X2}",
            _ => key.ToString(),
        };
    }
}
