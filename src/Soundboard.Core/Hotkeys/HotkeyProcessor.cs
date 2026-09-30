namespace Soundboard.Core.Hotkeys;

/// <summary>
/// Tracks held keys and matches them against bound hotkeys. Pure logic (no Win32) so it can be unit tested;
/// <see cref="KeyboardHook"/> feeds it events.
/// </summary>
/// <remarks>
/// A hotkey fires on the key-down that makes the held set exactly equal to the binding.
/// If both "A" and "A + B" are bound, pressing A fires "A", then pressing B while holding A fires "A + B".
/// </remarks>
public sealed class HotkeyProcessor
{
    private readonly HashSet<int> _down = [];
    private readonly HashSet<int> _suppressed = [];
    private readonly Func<int, bool>? _isPhysicallyDown;
    private HashSet<Hotkey> _bindings = [];

    private Action<Hotkey?>? _recordCallback;
    private readonly List<int> _recorded = [];

    /// <param name="isPhysicallyDown">
    /// Optional check against the real keyboard state, used to forget keys whose key-up we never saw
    /// (e.g. released while a UAC prompt or another secure desktop had focus).
    /// </param>
    public HotkeyProcessor(Func<int, bool>? isPhysicallyDown = null)
    {
        _isPhysicallyDown = isPhysicallyDown;
    }

    /// <summary>When true, the key that completes a hotkey is swallowed and not passed to the focused app.</summary>
    public bool SuppressMatchedKeys { get; set; }

    public bool IsRecording => _recordCallback is not null;

    public event Action<Hotkey>? Triggered;

    public void SetBindings(IEnumerable<Hotkey> bindings) => _bindings = [.. bindings];

    /// <summary>
    /// Captures the next combination the user presses (up to two keys, finished when all are released).
    /// The callback receives null if Escape is pressed.
    /// </summary>
    public void BeginRecording(Action<Hotkey?> callback)
    {
        CancelRecording();
        _recorded.Clear();
        _recordCallback = callback;
    }

    public void CancelRecording() => FinishRecording(null);

    /// <returns>True if the key should be suppressed.</returns>
    public bool OnKeyDown(int vk)
    {
        vk = VirtualKeys.Normalize(vk);
        ForgetStaleKeys(except: vk);

        if (!_down.Add(vk))
        {
            // Auto-repeat: suppress repeats of a swallowed key, otherwise let them through.
            return IsRecording || _suppressed.Contains(vk);
        }

        if (IsRecording)
        {
            if (vk == VirtualKeys.Escape)
            {
                FinishRecording(null);
            }
            else if (_recorded.Count < 2 && !_recorded.Contains(vk))
            {
                _recorded.Add(vk);
            }
            return true;
        }

        if (_down.Count <= 2 && Hotkey.From(_down) is { } combo && _bindings.Contains(combo))
        {
            Triggered?.Invoke(combo);
            if (SuppressMatchedKeys)
            {
                _suppressed.Add(vk);
                return true;
            }
        }
        return false;
    }

    /// <returns>True if the key should be suppressed.</returns>
    public bool OnKeyUp(int vk)
    {
        vk = VirtualKeys.Normalize(vk);
        _down.Remove(vk);

        if (IsRecording)
        {
            if (_down.Count == 0 && _recorded.Count > 0)
            {
                FinishRecording(Hotkey.From(_recorded));
            }
            return true;
        }

        return _suppressed.Remove(vk);
    }

    private void FinishRecording(Hotkey? result)
    {
        var callback = _recordCallback;
        _recordCallback = null;
        _recorded.Clear();
        callback?.Invoke(result);
    }

    private void ForgetStaleKeys(int except)
    {
        // Keys we swallowed never reach the system key state, so they can't be checked this way.
        if (_isPhysicallyDown is null || _down.Count == 0 || IsRecording)
        {
            return;
        }
        _down.RemoveWhere(k => k != except && !_suppressed.Contains(k) && !_isPhysicallyDown(k));
    }
}
