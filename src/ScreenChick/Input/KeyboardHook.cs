using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenChick.Native;

namespace ScreenChick.Input;

/// <summary>
/// Global keyboard hook on its own thread, so a busy UI thread can never add typing lag
/// or get the hook silently dropped by Windows for being slow.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly KeyRouter _router;
    private readonly Action<KeyAction> _onAction;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private int _installError;

    public KeyboardHook(KeyRouter router, Action<KeyAction> onAction)
    {
        _router = router;
        _onAction = onAction;
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "KeyboardHook" };
        _thread.Start();
        _started.Wait();
        if (_installError != 0) throw new Win32Exception(_installError);
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        var hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (hook == 0) _installError = Marshal.GetLastWin32Error();
        _started.Set();
        if (hook == 0) return;

        while (NativeMethods.GetMessage(out _, 0, 0, 0) > 0) { }
        NativeMethods.UnhookWindowsHookEx(hook);
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var isDown = wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var result = _router.Process(new KeyInput(
                info.vkCode, isDown, CurrentMods(), info.dwExtraInfo == InputInjector.OwnInputTag));

            if (result.Action != KeyAction.None) _onAction(result.Action);
            if (result.Swallow) return 1;
        }
        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }

    private static KeyMods CurrentMods()
    {
        var mods = KeyMods.None;
        if (IsDown(NativeMethods.VK_CONTROL)) mods |= KeyMods.Ctrl;
        if (IsDown(NativeMethods.VK_SHIFT)) mods |= KeyMods.Shift;
        if (IsDown(NativeMethods.VK_MENU)) mods |= KeyMods.Alt;
        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN)) mods |= KeyMods.Win;
        return mods;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, 0, 0);
        _thread.Join(500);
    }
}
