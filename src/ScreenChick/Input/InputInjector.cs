using System.Runtime.InteropServices;
using ScreenChick.Native;

namespace ScreenChick.Input;

internal static class InputInjector
{
    /// <summary>Marks our own keystrokes so the hook lets them through instead of re-triggering a paste.</summary>
    public static readonly nuint OwnInputTag = 0x5343484B;

    public static void SendCtrlV()
    {
        Send(
            Key(NativeMethods.VK_CONTROL, up: false),
            Key(KeyRouter.VkV, up: false),
            Key(KeyRouter.VkV, up: true),
            Key(NativeMethods.VK_CONTROL, up: true));
    }

    /// <summary>
    /// Windows only lets the process that received the last input take the foreground.
    /// The hook swallowed that input, so a zero-move mouse event earns the right back.
    /// </summary>
    public static void TakeForeground(nint hwnd)
    {
        Send(new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE });
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public static bool IsKeyHeld(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static NativeMethods.INPUT Key(int vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = OwnInputTag,
            },
        },
    };

    private static void Send(params NativeMethods.INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
}
