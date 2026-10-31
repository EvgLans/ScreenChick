using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ScreenChick.Localization;
using ScreenChick.Model;
using ScreenChick.Native;
using ScreenChick.Rendering;

namespace ScreenChick.UI;

public sealed record StripRow(int Index, string Letter, string Caption, BitmapSource Thumbnail);

/// <summary>
/// The side strip of pending shots. It never takes focus, so the chat keeps the caret
/// and Ctrl+V or the Paste button land there. It is also invisible to screen capture.
/// </summary>
public partial class StripWindow : Window
{
    private bool _positioned;

    public event Action? NewShotRequested;
    public event Action? PasteRequested;
    public event Action? ClearRequested;
    public event Action<int>? EditRequested;
    public event Action<int>? RemoveRequested;

    public StripWindow()
    {
        InitializeComponent();
        RowsScroll.MaxHeight = SystemParameters.WorkArea.Height * 0.6;
        SourceInitialized += (_, _) => ApplyWindowStyles();
        SizeChanged += (_, _) => KeepDockedIfUnmoved();
    }

    public void Refresh(ShotQueue queue)
    {
        var shots = queue.Shots;
        Rows.ItemsSource = shots.Select((shot, i) => new StripRow(
            i,
            ShotQueue.LetterOf(i).ToString(),
            L.Comments(shot.State.CommentCount),
            ShotRenderer.RenderThumbnail(shot, i, 104, 68))).ToList();

        CountText.Text = shots.Count.ToString();
    }

    public void SetStatus(string text, bool armed)
    {
        StatusText.Text = text;
        PasteButton.Content = armed ? L.StripPasteArmed : L.StripPaste;
    }

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    private void KeepDockedIfUnmoved()
    {
        if (_positioned) return;
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        _positioned = true;
        DragMove();
    }

    private void OnRowClick(object sender, RoutedEventArgs e) => EditRequested?.Invoke((int)((Button)sender).Tag);
    private void OnRowRemove(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke((int)((Button)sender).Tag);
    private void OnNewShot(object sender, RoutedEventArgs e) => NewShotRequested?.Invoke();
    private void OnPaste(object sender, RoutedEventArgs e) => PasteRequested?.Invoke();
    private void OnClear(object sender, RoutedEventArgs e) => ClearRequested?.Invoke();
}
