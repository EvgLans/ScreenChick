using System.Runtime.InteropServices;
using System.Windows;
using ScreenChick.Input;
using ScreenChick.Model;
using ScreenChick.Native;
using ScreenChick.Rendering;

namespace ScreenChick.Paste;

internal enum PasteOutcome { Completed, FocusLost, ClipboardBusy }

/// <summary>
/// Chats accept one image per clipboard read, so "one Ctrl+V" is really a rapid series:
/// each shot goes through the clipboard into the field, then the comments.
/// Nothing is ever sent; the user presses Send.
/// </summary>
internal static class PasteSequencer
{
    private const int MaxWaitForKeyReleaseMs = 1000;
    private const int ClipboardSettleMs = 30;
    private const int TextSettleMs = 120;

    public static async Task<PasteOutcome> RunAsync(IReadOnlyList<Shot> shots, int delayBetweenImagesMs)
    {
        await WaitForKeysReleasedAsync();

        // The target is whatever has focus now. If that changes mid-way we stop,
        // rather than dropping private screenshots into some other chat.
        var target = NativeMethods.GetForegroundWindow();

        for (var i = 0; i < shots.Count; i++)
        {
            var final = ShotRenderer.RenderFinal(shots[i], i);
            var (png, dib) = await Task.Run(() =>
                (ClipboardPayload.EncodePng(final), ClipboardPayload.EncodeDib(final)));

            if (NativeMethods.GetForegroundWindow() != target) return PasteOutcome.FocusLost;
            if (!TrySetClipboard(ClipboardPayload.ForImage(png, dib))) return PasteOutcome.ClipboardBusy;

            await Task.Delay(ClipboardSettleMs);
            if (NativeMethods.GetForegroundWindow() != target) return PasteOutcome.FocusLost;
            InputInjector.SendCtrlV();
            await Task.Delay(delayBetweenImagesMs);
        }

        var text = MessageFormatter.Format(shots.Select(s => s.State).ToList());
        if (text.Length == 0) return PasteOutcome.Completed;

        if (NativeMethods.GetForegroundWindow() != target) return PasteOutcome.FocusLost;
        if (!TrySetClipboard(ClipboardPayload.ForText(text))) return PasteOutcome.ClipboardBusy;

        await Task.Delay(ClipboardSettleMs);
        if (NativeMethods.GetForegroundWindow() != target) return PasteOutcome.FocusLost;
        InputInjector.SendCtrlV();
        await Task.Delay(TextSettleMs);
        return PasteOutcome.Completed;
    }

    /// <summary>Injecting while the user still holds Ctrl would leave their keyboard state confused.</summary>
    private static async Task WaitForKeysReleasedAsync()
    {
        var waited = 0;
        while (waited < MaxWaitForKeyReleaseMs &&
               (InputInjector.IsKeyHeld(NativeMethods.VK_CONTROL) || InputInjector.IsKeyHeld(KeyRouter.VkV)))
        {
            await Task.Delay(15);
            waited += 15;
        }
    }

    private static bool TrySetClipboard(DataObject data)
    {
        try
        {
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
