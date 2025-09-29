using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyLockr.Tray.Interop;

namespace KeyLockr.Tray;

internal readonly struct HotkeyDefinition
{
    public HotkeyDefinition(HotKeyNative.Modifiers modifiers, Keys key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public HotKeyNative.Modifiers Modifiers { get; }
    public Keys Key { get; }

    public uint VirtualKey => (uint)(Key & Keys.KeyCode);
}

internal static class HotkeyParser
{
    public static bool TryParse(string? text, out HotkeyDefinition definition, out string? error)
    {
        definition = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "未设置快捷键。";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "快捷键格式无效。";
            return false;
        }

        HotKeyNative.Modifiers modifiers = HotKeyNative.Modifiers.None;
        Keys key = Keys.None;

        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotKeyNative.Modifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotKeyNative.Modifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotKeyNative.Modifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotKeyNative.Modifiers.Win;
                    break;
                default:
                    if (!TryParseKey(token, out key))
                    {
                        error = $"无法识别按键 '{token}'。";
                        return false;
                    }
                    break;
            }
        }

        if (key == Keys.None)
        {
            error = "必须指定一个触发按键。";
            return false;
        }

        definition = new HotkeyDefinition(modifiers, key);
        return true;
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        if (Enum.TryParse(token, true, out Keys parsed))
        {
            key = parsed & Keys.KeyCode;
            return true;
        }

        if (token.Length == 1)
        {
            var upper = char.ToUpperInvariant(token[0]);
            if (upper is >= 'A' and <= 'Z')
            {
                key = (Keys)upper;
                return true;
            }

            if (upper is >= '0' and <= '9')
            {
                key = (Keys)upper;
                return true;
            }
        }

        if (token.StartsWith("F", true, CultureInfo.InvariantCulture) && int.TryParse(token[1..], out var functionIndex))
        {
            if (functionIndex is >= 1 and <= 24)
            {
                key = Keys.F1 + (functionIndex - 1);
                return true;
            }
        }

        return false;
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly int _hotkeyId;
    private readonly Action _callback;

    public HotkeyWindow(Action callback)
    {
        _callback = callback;
        _hotkeyId = GetHashCode() & 0xFFFF;
        CreateHandle(new CreateParams());
    }

    public bool Register(HotkeyDefinition definition)
    {
        Unregister();
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Hotkey window handle is not initialized.");
        }

        if (!HotKeyNative.RegisterHotKey(Handle, _hotkeyId, definition.Modifiers, definition.VirtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            throw new InvalidOperationException($"注册全局快捷键失败：{error.Message}");
        }

        return true;
    }

    public void Unregister()
    {
        if (Handle != IntPtr.Zero)
        {
            HotKeyNative.UnregisterHotKey(Handle, _hotkeyId);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotKeyNative.WmHotKey && m.WParam.ToInt32() == _hotkeyId)
        {
            _callback();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
