using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Soundboard.Core.Hotkeys;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). Must be installed on a thread that pumps
/// messages (the WPF UI thread does). Windows silently removes the hook if the callback takes
/// longer than ~1 s, so keep handlers fast and don't set breakpoints inside them.
/// </summary>
/// <remarks>
/// Keys pressed while an elevated (admin) window has focus are only seen if this process is elevated too.
/// </remarks>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly HotkeyProcessor _processor;
    private readonly LowLevelKeyboardProc _proc; // Kept in a field so the GC doesn't collect the delegate.
    private IntPtr _hook;

    public KeyboardHook(HotkeyProcessor processor)
    {
        _processor = processor;
        _proc = HookProc;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public static bool IsKeyPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // Ignore synthetic input (e.g. our own future push-to-talk key presses).
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                bool suppress = (int)wParam switch
                {
                    WM_KEYDOWN or WM_SYSKEYDOWN => _processor.OnKeyDown((int)data.vkCode),
                    WM_KEYUP or WM_SYSKEYUP => _processor.OnKeyUp((int)data.vkCode),
                    _ => false,
                };
                if (suppress)
                {
                    return 1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
