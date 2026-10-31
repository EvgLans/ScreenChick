using System.Windows.Media.Imaging;

namespace ScreenChick.Model;

/// <summary>
/// A frozen screen image and its edit history. The original pixels stay in memory only;
/// whatever leaves the app is flattened by ShotRenderer, so redactions cannot be undone by the reader.
/// </summary>
public sealed class Shot(BitmapSource image, double uiScale)
{
    public BitmapSource Image { get; } = image;

    /// <summary>Monitor scale at capture time; keeps badges the same visual size on any display.</summary>
    public double UiScale { get; } = uiScale;

    public UndoStack<ShotState> History { get; } = new(ShotState.Empty);
    public ShotState State => History.Current;
    public PixelRect FullBounds => new(0, 0, Image.PixelWidth, Image.PixelHeight);
    public PixelRect VisibleBounds => State.Crop ?? FullBounds;
}
