using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyLockr.Core.Interop;

namespace KeyLockr.Core.Services;

public sealed class SoftKeyboardBlocker : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly User32Native.LowLevelKeyboardProc _proc;
    private volatile bool _isBlocking = false;
    private readonly object _lock = new object();
    private bool _disposed = false;
    private int _blockedKeyCount = 0;
    private bool _aggressive = true;

    public SoftKeyboardBlocker()
    {
        _proc = HookCallback;
    }

    public void StartBlocking()
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SoftKeyboardBlocker));

            if (_isBlocking)
                return;

            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装键盘钩子");
            }

            _isBlocking = true;
        }
    }

    public void StopBlocking()
    {
        lock (_lock)
        {
            if (!_isBlocking || _disposed)
                return;

            if (_hookId != IntPtr.Zero)
            {
                User32Native.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            _isBlocking = false;
        }
    }

    public bool IsBlocking => _isBlocking;
    
    public int BlockedKeyCount => _blockedKeyCount;

    public void SetAggressive(bool aggressive)
    {
        _aggressive = aggressive;
    }

    private static IntPtr SetHook(User32Native.LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule?.ModuleName == null)
            return IntPtr.Zero;

        return User32Native.SetWindowsHookEx(
            User32Native.WH_KEYBOARD_LL,
            proc,
            User32Native.GetModuleHandle(curModule.ModuleName),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isBlocking)
        {
            // Check if this is a keyboard event we want to block
            var message = wParam.ToInt32();
            if (message == User32Native.WM_KEYDOWN || 
                message == User32Native.WM_KEYUP || 
                message == User32Native.WM_SYSKEYDOWN || 
                message == User32Native.WM_SYSKEYUP)
            {
                var hookStruct = Marshal.PtrToStructure<User32Native.KBDLLHOOKSTRUCT>(lParam);
                
                // Block the key if it appears to be from internal keyboard
                if (ShouldBlockKey(hookStruct))
                {
                    Interlocked.Increment(ref _blockedKeyCount);
                    return new IntPtr(1); // Block the key
                }
            }
        }

        return User32Native.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ShouldBlockKey(User32Native.KBDLLHOOKSTRUCT hookStruct)
    {
        var vkCode = hookStruct.vkCode;
        var flags = hookStruct.flags;
        
        // First, check if this might be from an external keyboard
        // External keyboards often have these characteristics:
        
        // 1. Allow injected keys (from external software/devices)
        if ((flags & 0x01) != 0) // LLKHF_INJECTED
        {
            return false;
        }
        
        // 2. Allow Alt+F4 for emergency window closing
        if (vkCode == 0x73 && // F4
            (GetAsyncKeyState(0x12) & 0x8000) != 0) // Alt is pressed
        {
            return false;
        }
        
        // 3. Allow Ctrl+Alt+K (our unlock hotkey)
        if (vkCode == 0x4B && // K key
            (GetAsyncKeyState(0x11) & 0x8000) != 0 && // Ctrl is pressed
            (GetAsyncKeyState(0x12) & 0x8000) != 0)   // Alt is pressed
        {
            return false;
        }
        
        // 4. Allow Ctrl+Alt+Del (system security)
        if (vkCode == 0x2E && // Delete key
            (GetAsyncKeyState(0x11) & 0x8000) != 0 && // Ctrl is pressed
            (GetAsyncKeyState(0x12) & 0x8000) != 0)   // Alt is pressed
        {
            return false;
        }
        
        // Check scan code patterns that might indicate external keyboards
        var scanCode = hookStruct.scanCode;
        
        // Some external keyboards use different scan code ranges
        // This is a heuristic and may need adjustment
        if (scanCode > 0x80) // Extended scan codes often from external devices
        {
            return false;
        }
        
        if (_aggressive)
        {
            // Block everything else - aggressive mode
            return true;
        }

        // Non-aggressive: block only common typing keys
        // Letters, numbers, space, backspace, enter, arrows
        if ((vkCode >= 0x30 && vkCode <= 0x5A) || // 0-9, A-Z
            vkCode == 0x20 || // Space
            vkCode == 0x08 || // Backspace
            vkCode == 0x0D || // Enter
            (vkCode >= 0x25 && vkCode <= 0x28)) // Arrows
        {
            return true;
        }

        return false;
    }
    
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void Dispose()
    {
        if (_disposed)
            return;

        StopBlocking();
        _disposed = true;
    }
}
