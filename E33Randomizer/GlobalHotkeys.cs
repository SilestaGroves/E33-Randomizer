using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace E33Randomizer;

/// <summary>
/// System-wide hotkeys (RegisterHotKey), so the tracker can be shown and hidden while the game has the focus.
/// They work as long as the randomizer is running.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();

    public GlobalHotkeys(Window window)
    {
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    /// <summary>Registers (or re-registers) a hotkey. Returns false if the text isn't a hotkey or another program has it.</summary>
    public bool Register(int id, string hotkey, Action action)
    {
        Unregister(id);
        if (!TryParse(hotkey, out var modifiers, out var key)) return false;
        uint mods = ModNoRepeat;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModWin;
        if (!RegisterHotKey(_handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            Log.Warn($"Couldn't register the hotkey {hotkey} (error {Marshal.GetLastWin32Error()})");
            return false;
        }
        _actions[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id)) UnregisterHotKey(_handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Reads "Ctrl+Shift+T". A hotkey needs a modifier, except for the function keys.</summary>
    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        if (!Enum.TryParse(parts[^1], true, out key) || IsModifier(key) || key == Key.None) return false;
        return modifiers != ModifierKeys.None || key is >= Key.F1 and <= Key.F24;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    public void Dispose()
    {
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
        _source?.RemoveHook(WndProc);
    }
}
