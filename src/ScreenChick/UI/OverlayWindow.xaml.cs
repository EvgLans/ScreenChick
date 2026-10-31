using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenChick.Capture;
using ScreenChick.Input;
using ScreenChick.Model;
using ScreenChick.Native;
using ScreenChick.Rendering;

namespace ScreenChick.UI;

public partial class OverlayWindow : Window
{
    private const int ClickThreshold = 4;
    private const int MinCropSize = 16;
    private const int MinBrush = 8;
    private const int MaxBrush = 160;

    private enum DragKind { None, Mark, Redact, Crop }

    private readonly Shot _shot;
    private readonly int _shotIndex;
    private readonly PixelBounds _monitor;
    private readonly ShotState _stateWhenOpened;

    private DragKind _drag;
    private PixelPoint _dragStart;
    private List<PixelPoint>? _strokePoints;
    private int _brush;

    private int _editIndex = -1;
    private bool _editIsNew;
    private bool _escapeArmed;
    private bool _finished;

    /// <summary>Raised once. True means keep the shot; false means the edits were discarded.</summary>
    public event Action<bool>? Finished;

    internal OverlayWindow(Shot shot, int shotIndex, PixelBounds monitor)
    {
        InitializeComponent();
        _shot = shot;
        _shotIndex = shotIndex;
        _monitor = monitor;
        _stateWhenOpened = shot.State;
        _brush = (int)(22 * shot.UiScale);

        Surface.Shot = shot;
        Surface.ShotIndex = shotIndex;
        Surface.BrushThickness = _brush;
        RefreshHistoryButtons();
    }

    public void ShowOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        PlaceOnMonitor(hwnd);
        Show();
        // The first move can cross into a monitor with a different DPI, and WPF then resizes
        // from its suggested rectangle. Placing it again settles on the exact pixel bounds.
        PlaceOnMonitor(hwnd);
        InputInjector.TakeForeground(hwnd);
        Activate();
        Focus();
    }

    private void PlaceOnMonitor(nint hwnd) => NativeMethods.SetWindowPos(
        hwnd, NativeMethods.HWND_TOPMOST, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, 0);

    private bool IsEditorOpen => _editIndex >= 0;

    private PixelPoint ToImagePoint(MouseEventArgs e, PixelRect bounds)
    {
        var p = e.GetPosition(Surface);
        return bounds.Clamp(new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)));
    }

    private void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEditorOpen) CommitEditor();
        _escapeArmed = false;
        Toast.Visibility = Visibility.Collapsed;

        if (CropTool.IsChecked == true)
        {
            _drag = DragKind.Crop;
            _dragStart = ToImagePoint(e, _shot.FullBounds);
        }
        else if (RedactTool.IsChecked == true)
        {
            _drag = DragKind.Redact;
            _dragStart = ToImagePoint(e, _shot.VisibleBounds);
            _strokePoints = [_dragStart];
            Surface.LiveStroke = new RedactionStroke([.. _strokePoints], _brush);
        }
        else
        {
            _drag = DragKind.Mark;
            _dragStart = ToImagePoint(e, _shot.VisibleBounds);
        }

        Toolbar.Visibility = Visibility.Hidden;
        Surface.CaptureMouse();
        Surface.InvalidateVisual();
    }

    private void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        switch (_drag)
        {
            case DragKind.Mark:
                Surface.DragRect = DragRectOrNull(_dragStart, ToImagePoint(e, _shot.VisibleBounds));
                break;
            case DragKind.Crop:
                Surface.DragRect = PixelRect.FromCorners(_dragStart, ToImagePoint(e, _shot.FullBounds));
                break;
            case DragKind.Redact:
                _strokePoints!.Add(ToImagePoint(e, _shot.VisibleBounds));
                Surface.LiveStroke = new RedactionStroke([.. _strokePoints], _brush);
                break;
        }

        Surface.BrushHover = RedactTool.IsChecked == true ? ToImagePoint(e, _shot.FullBounds) : null;
        Surface.InvalidateVisual();
    }

    private void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragKind.None) return;

        var kind = _drag;
        _drag = DragKind.None;
        Surface.ReleaseMouseCapture();
        Toolbar.Visibility = Visibility.Visible;
        Surface.DragRect = null;
        Surface.LiveStroke = null;

        switch (kind)
        {
            case DragKind.Mark:
                FinishMark(ToImagePoint(e, _shot.VisibleBounds));
                break;
            case DragKind.Redact:
                _shot.History.Apply(_shot.State.AddRedaction(new RedactionStroke([.. _strokePoints!], _brush)));
                _strokePoints = null;
                break;
            case DragKind.Crop:
                FinishCrop(ToImagePoint(e, _shot.FullBounds));
                break;
        }

        RefreshHistoryButtons();
        Surface.InvalidateVisual();
    }

    private void FinishMark(PixelPoint end)
    {
        if (DragRectOrNull(_dragStart, end) is { } area)
        {
            _shot.History.Apply(_shot.State.AddMark(area));
            OpenEditor(_shot.State.Marks.Count - 1, isNew: true);
            return;
        }

        var existing = ShotRenderer.HitTest(
            _shot.State, _shotIndex, _shot.UiScale, _shot.VisibleBounds, new Point(end.X, end.Y));
        if (existing >= 0)
        {
            OpenEditor(existing, isNew: false);
            return;
        }

        _shot.History.Apply(_shot.State.AddMark(new PixelRect(end.X, end.Y, 0, 0)));
        OpenEditor(_shot.State.Marks.Count - 1, isNew: true);
    }

    private void FinishCrop(PixelPoint end)
    {
        var crop = PixelRect.FromCorners(_dragStart, end);
        if (crop.Width < MinCropSize || crop.Height < MinCropSize) return;

        _shot.History.Apply(_shot.State.ApplyCrop(crop));
        MarkTool.IsChecked = true;
    }

    private static PixelRect? DragRectOrNull(PixelPoint a, PixelPoint b)
    {
        var rect = PixelRect.FromCorners(a, b);
        return rect.Width <= ClickThreshold && rect.Height <= ClickThreshold ? null : rect;
    }

    private void OnSurfaceLeave(object sender, MouseEventArgs e)
    {
        if (_drag != DragKind.None) return;
        Surface.BrushHover = null;
        Surface.InvalidateVisual();
    }

    private void OnSurfaceWheel(object sender, MouseWheelEventArgs e)
    {
        if (RedactTool.IsChecked != true) return;
        _brush = Math.Clamp(_brush + Math.Sign(e.Delta) * (int)(4 * _shot.UiScale), MinBrush, MaxBrush);
        Surface.BrushThickness = _brush;
        Surface.InvalidateVisual();
    }

    private void OnToolChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (IsEditorOpen) CommitEditor();
        if (RedactTool.IsChecked != true) Surface.BrushHover = null;
        Surface.InvalidateVisual();
    }

    private void OpenEditor(int index, bool isNew)
    {
        _editIndex = index;
        _editIsNew = isNew;
        EditorLabel.Text = ShotQueue.LabelOf(_shotIndex, index);
        CommentBox.Text = _shot.State.Marks[index].Comment;
        CommentBox.CaretIndex = CommentBox.Text.Length;
        Surface.HiddenCommentIndex = index;

        Editor.Visibility = Visibility.Visible;
        Root.UpdateLayout();
        PositionEditor(_shot.State.Marks[index].Area);
        CommentBox.Focus();
        Keyboard.Focus(CommentBox);
    }

    private void PositionEditor(PixelRect area)
    {
        const double gap = 10;
        var below = Surface.TranslatePoint(new Point(area.X, area.Bottom), Root);
        var above = Surface.TranslatePoint(new Point(area.X, area.Y), Root);
        var width = Editor.ActualWidth;
        var height = Editor.ActualHeight;

        var x = Math.Clamp(below.X, gap, Math.Max(gap, Root.ActualWidth - width - gap));
        var y = below.Y + gap;
        if (y + height > Root.ActualHeight - gap) y = above.Y - height - gap;
        y = Math.Clamp(y, gap, Math.Max(gap, Root.ActualHeight - height - gap));

        Canvas.SetLeft(Editor, x);
        Canvas.SetTop(Editor, y);
    }

    private void CommitEditor()
    {
        var index = _editIndex;
        var isNew = _editIsNew;
        var text = CommentBox.Text.Trim();
        CloseEditor();

        var next = _shot.State.WithComment(index, text);
        if (isNew) _shot.History.ReplaceCurrent(next);
        else if (text != _shot.State.Marks[index].Comment) _shot.History.Apply(next);
    }

    private void CancelEditor()
    {
        var isNew = _editIsNew;
        CloseEditor();
        if (isNew) _shot.History.DropCurrent();
    }

    private void CloseEditor()
    {
        _editIndex = -1;
        _editIsNew = false;
        Surface.HiddenCommentIndex = -1;
        Editor.Visibility = Visibility.Collapsed;
        Focus();
        RefreshHistoryButtons();
        Surface.InvalidateVisual();
    }

    private void OnDeleteMark(object sender, RoutedEventArgs e)
    {
        var index = _editIndex;
        var isNew = _editIsNew;
        CloseEditor();
        if (isNew) _shot.History.DropCurrent();
        else _shot.History.Apply(_shot.State.RemoveMark(index));
        RefreshHistoryButtons();
        Surface.InvalidateVisual();
    }

    private void OnCommentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelEditor();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsEditorOpen || e.Handled) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl && e.Key == Key.Z && !shift) Undo();
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) Redo();
        else if (e.Key == Key.D1 || e.Key == Key.NumPad1) MarkTool.IsChecked = true;
        else if (e.Key == Key.D2 || e.Key == Key.NumPad2) RedactTool.IsChecked = true;
        else if (e.Key == Key.D3 || e.Key == Key.NumPad3) CropTool.IsChecked = true;
        else if (e.Key == Key.Enter) Finish(keep: true);
        else if (e.Key == Key.Escape) RequestCancel();
        else return;

        e.Handled = true;
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void OnDone(object sender, RoutedEventArgs e)
    {
        if (IsEditorOpen) CommitEditor();
        Finish(keep: true);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (IsEditorOpen) CancelEditor();
        RequestCancel();
    }

    private void Undo()
    {
        if (IsEditorOpen) CommitEditor();
        _shot.History.Undo();
        RefreshHistoryButtons();
        Surface.InvalidateVisual();
    }

    private void Redo()
    {
        if (IsEditorOpen) CommitEditor();
        _shot.History.Redo();
        RefreshHistoryButtons();
        Surface.InvalidateVisual();
    }

    /// <summary>Marked-up work should not vanish on a stray Esc, so it takes a second press.</summary>
    private void RequestCancel()
    {
        if (ReferenceEquals(_shot.State, _stateWhenOpened) || _escapeArmed)
        {
            Finish(keep: false);
            return;
        }

        _escapeArmed = true;
        ToastText.Text = "Правки пропадут. Нажми Esc ещё раз, чтобы выйти без сохранения";
        Toast.Visibility = Visibility.Visible;
    }

    private void Finish(bool keep)
    {
        if (_finished) return;
        _finished = true;
        if (!keep) _shot.History.Apply(_stateWhenOpened);
        Close();
        Finished?.Invoke(keep);
    }

    private void RefreshHistoryButtons()
    {
        UndoButton.IsEnabled = _shot.History.CanUndo;
        RedoButton.IsEnabled = _shot.History.CanRedo;
    }
}
