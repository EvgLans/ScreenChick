using System.Windows;
using System.Windows.Threading;
using ScreenChick.Capture;
using ScreenChick.Input;
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
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ShotQueue _queue = new();
    private readonly StripWindow _strip = new();
    private readonly DispatcherTimer _clipboardWatch = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon _tray;
    private readonly KeyRouter _router;
    private readonly KeyboardHook _hook;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

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

        _strip.NewShotRequested += StartNewShot;
        _strip.PasteRequested += OnPasteButton;
        _strip.ClearRequested += _queue.Clear;
        _strip.EditRequested += EditShot;
        _strip.RemoveRequested += _queue.RemoveAt;
        _queue.Changed += OnQueueChanged;

        _clipboardWatch.Tick += (_, _) => RefreshStripStatus();

        _router = new KeyRouter(DecidePaste) { Hotkey = _settings.Hotkey };
        _hook = new KeyboardHook(_router, OnKeyAction);

        _tray.ShowBalloonTip(3000, "Кря! ScreenChick запущен",
            $"{_settings.Hotkey.Display}: новый снимок. Значок в трее: настройки и выход.", Forms.ToolTipIcon.None);
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
            Notify($"В ленте уже {ShotQueue.MaxShots} снимков. Убери лишние или очисти ленту.");
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
        var summary = MessageFormatter.Plural(_queue.Count, "снимок", "снимка", "снимков") + " · " +
                      MessageFormatter.Plural(comments, "комментарий", "комментария", "комментариев");

        _strip.SetStatus(armed ? summary : summary + "\nТы что-то скопировал, поэтому Ctrl+V вставит это. Снимки: кнопка выше.", armed);
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
                    Notify("Вставка остановлена: ты переключил окно. Снимки остались в ленте.");
                    break;
                case PasteOutcome.ClipboardBusy:
                    Notify("Буфер обмена занят другой программой. Снимки остались в ленте, попробуй ещё раз.");
                    break;
            }
        }
        finally
        {
            ArmClipboard();
            Volatile.Write(ref _pasting, 0);
        }
    }

    private Forms.NotifyIcon CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Новый снимок", null, (_, _) => StartNewShot());
        menu.Items.Add("Очистить ленту", null, (_, _) => _queue.Clear());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add("Выход", null, (_, _) => Application.Current.Shutdown());

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "ScreenChick",
            ContextMenuStrip = menu,
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
        _settingsWindow.Saved += () => _router.Hotkey = _settings.Hotkey;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
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
