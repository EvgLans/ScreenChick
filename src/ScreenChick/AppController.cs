using System.Windows;
using System.Windows.Threading;
using ScreenChick.Capture;
using ScreenChick.Input;
using ScreenChick.Localization;
using ScreenChick.Model;
using ScreenChick.Native;
using ScreenChick.Paste;
using ScreenChick.Settings;
using ScreenChick.UI;
using Forms = System.Windows.Forms;

namespace ScreenChick;

/// <summary>Wires the tray, keyboard hook, overlay, strip and paste sequence together.</summary>
internal sealed class AppController : IDisposable
{
    private readonly AppSettings _settings = LoadSettings();
    private readonly ShotQueue _queue = new();
    private readonly DispatcherTimer _clipboardWatch = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon _tray;
    private readonly KeyRouter _router;
    private readonly KeyboardHook _hook;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    private StripWindow _strip;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;

    // Read from the hook thread.
    private volatile int _queueCount;
    private volatile bool _overlayOpen;
    private int _pasting;
    private uint _clipboardBaseline;

    public AppController()
    {
        _tray = CreateTray();

        _strip = CreateStrip();
        _queue.Changed += OnQueueChanged;

        _clipboardWatch.Tick += (_, _) => RefreshStripStatus();

        _router = new KeyRouter(DecidePaste) { Hotkey = _settings.Hotkey };
        _hook = new KeyboardHook(_router, OnKeyAction);

        _tray.ShowBalloonTip(3000, L.StartedTitle, L.StartedBody(_settings.Hotkey.Display), Forms.ToolTipIcon.None);
    }

    /// <summary>Runs before the windows are created, because they read their text once.</summary>
    private static AppSettings LoadSettings()
    {
        var settings = AppSettings.Load();
        L.Use(settings.Language);
        return settings;
    }

    private StripWindow CreateStrip()
    {
        var strip = new StripWindow();
        strip.NewShotRequested += StartNewShot;
        strip.PasteRequested += OnPasteButton;
        strip.ClearRequested += _queue.Clear;
        strip.EditRequested += EditShot;
        strip.RemoveRequested += _queue.RemoveAt;
        return strip;
    }

    /// <summary>
    /// Ctrl+V belongs to the shots only while nothing newer has been copied. If the user copies
    /// a link in between, Ctrl+V pastes that link, and the strip's button still pastes the shots.
    /// </summary>
    private PasteDecision DecidePaste()
    {
        if (Volatile.Read(ref _pasting) != 0) return PasteDecision.Swallow;
        if (_overlayOpen || _queueCount == 0) return PasteDecision.Pass;
        if (NativeMethods.GetClipboardSequenceNumber() != Volatile.Read(ref _clipboardBaseline)) return PasteDecision.Pass;
        if (IsOwnWindowForeground()) return PasteDecision.Pass;

        return Interlocked.CompareExchange(ref _pasting, 1, 0) == 0 ? PasteDecision.Trigger : PasteDecision.Swallow;
    }

    private bool IsOwnWindowForeground()
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var pid);
        return pid == _ownProcessId;
    }

    private void OnKeyAction(KeyAction action)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (action)
            {
                case KeyAction.Capture:
                    StartNewShot();
                    break;
                case KeyAction.Paste:
                    _ = RunPasteAsync();
                    break;
                case KeyAction.Recorded:
                    if (_router.LastRecorded is { } hotkey) _settingsWindow?.OnHotkeyRecorded(hotkey);
                    break;
            }
        });
    }

    private void StartNewShot()
    {
        if (_overlay != null || Volatile.Read(ref _pasting) != 0) return;

        if (_queue.IsFull)
        {
            Notify(L.StripFull(ShotQueue.MaxShots));
            return;
        }

        var previous = NativeMethods.GetForegroundWindow();
        var snapshot = ScreenCapture.CaptureMonitorUnderCursor();
        var shot = new Shot(snapshot.Image, snapshot.UiScale);

        OpenOverlay(shot, _queue.Count, snapshot.Bounds, previous, keep =>
        {
            if (keep) _queue.Add(shot);
        });
    }

    private void EditShot(int index)
    {
        if (_overlay != null || Volatile.Read(ref _pasting) != 0) return;

        var previous = NativeMethods.GetForegroundWindow();
        var shot = _queue.Shots[index];
        var snapshotBounds = CurrentMonitorBounds();

        OpenOverlay(shot, index, snapshotBounds, previous, _ => _queue.NotifyEdited());
    }

    private static PixelBounds CurrentMonitorBounds()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var b = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).Bounds;
        return new PixelBounds(b.X, b.Y, b.Width, b.Height);
    }

    private void OpenOverlay(Shot shot, int index, PixelBounds bounds, nint previous, Action<bool> onFinished)
    {
        _strip.Hide();
        _clipboardWatch.Stop();
        _overlayOpen = true;

        _overlay = new OverlayWindow(shot, index, bounds);
        _overlay.Finished += keep =>
        {
            _overlay = null;
            _overlayOpen = false;
            onFinished(keep);
            ArmClipboard();
            RestoreForeground(previous);
            RefreshStripVisibility();
        };
        _overlay.ShowOnMonitor();
    }

    private static void RestoreForeground(nint window)
    {
        if (window != 0) InputInjector.TakeForeground(window);
    }

    private void OnQueueChanged()
    {
        _queueCount = _queue.Count;
        if (_overlay == null) RefreshStripVisibility();
    }

    private void RefreshStripVisibility()
    {
        if (_queue.Count == 0)
        {
            _strip.Hide();
            _clipboardWatch.Stop();
            return;
        }

        _strip.Refresh(_queue);
        RefreshStripStatus();
        if (!_strip.IsVisible) _strip.Show();
        _clipboardWatch.Start();
    }

    private void RefreshStripStatus()
    {
        var armed = NativeMethods.GetClipboardSequenceNumber() == Volatile.Read(ref _clipboardBaseline);
        var comments = _queue.Shots.Sum(s => s.State.CommentCount);
        var summary = L.Shots(_queue.Count) + " · " + L.Comments(comments);

        _strip.SetStatus(armed ? summary : summary + "\n" + L.StripCopiedNotice, armed);
    }

    private void ArmClipboard() =>
        Volatile.Write(ref _clipboardBaseline, NativeMethods.GetClipboardSequenceNumber());

    private void OnPasteButton()
    {
        if (Interlocked.CompareExchange(ref _pasting, 1, 0) != 0) return;
        _ = RunPasteAsync();
    }

    /// <summary>Shots stay in the strip after pasting, so they can go into several chats until the user clears them.</summary>
    private async Task RunPasteAsync()
    {
        try
        {
            var outcome = await PasteSequencer.RunAsync(_queue.Shots.ToList(), _settings.PasteDelayMs);
            switch (outcome)
            {
                case PasteOutcome.FocusLost:
                    Notify(L.PasteStoppedFocus);
                    break;
                case PasteOutcome.ClipboardBusy:
                    Notify(L.PasteStoppedClipboard);
                    break;
            }
        }
        finally
        {
            ArmClipboard();
            Volatile.Write(ref _pasting, 0);
        }
    }

    private Forms.ContextMenuStrip CreateMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L.MenuNewShot, null, (_, _) => StartNewShot());
        menu.Items.Add(L.MenuClear, null, (_, _) => _queue.Clear());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.MenuSettings, null, (_, _) => OpenSettings());
        menu.Items.Add(L.MenuExit, null, (_, _) => Application.Current.Shutdown());
        return menu;
    }

    private Forms.NotifyIcon CreateTray()
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "ScreenChick",
            ContextMenuStrip = CreateMenu(),
            Visible = true,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) StartNewShot();
        };
        return icon;
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _router);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved()
    {
        _router.Hotkey = _settings.Hotkey;
        if (!L.Use(_settings.Language)) return;

        // The shots live in the queue, so swapping the strip loses nothing.
        _strip.Close();
        _strip = CreateStrip();
        var oldMenu = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = CreateMenu();
        oldMenu?.Dispose();
        if (_overlay == null) RefreshStripVisibility();
    }

    private void Notify(string message) =>
        _tray.ShowBalloonTip(4000, "ScreenChick", message, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _hook.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
