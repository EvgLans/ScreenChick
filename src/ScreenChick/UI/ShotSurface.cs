using System.Windows;
using System.Windows.Media;
using ScreenChick.Model;
using ScreenChick.Rendering;

namespace ScreenChick.UI;

/// <summary>The frozen image plus annotations, laid out in image pixels. A Viewbox maps it onto the window.</summary>
public sealed class ShotSurface : FrameworkElement
{
    private static readonly Brush CropDim = MakeBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly Brush PreviewFill = MakeBrush(Color.FromArgb(0x22, 0xF4, 0x51, 0x1E));
    private static readonly Color PreviewColor = Color.FromRgb(0xF4, 0x51, 0x1E);

    public Shot? Shot { get; set; }
    public int ShotIndex { get; set; }
    public int HiddenCommentIndex { get; set; } = -1;

    public PixelRect? DragRect { get; set; }
    public RedactionStroke? LiveStroke { get; set; }
    public PixelPoint? BrushHover { get; set; }
    public int BrushThickness { get; set; }

    protected override Size MeasureOverride(Size availableSize) =>
        Shot == null ? default : new Size(Shot.Image.PixelWidth, Shot.Image.PixelHeight);

    protected override void OnRender(DrawingContext dc)
    {
        if (Shot == null) return;

        var state = Shot.State;
        if (LiveStroke != null) state = state.AddRedaction(LiveStroke);

        dc.DrawImage(Shot.Image, new Rect(0, 0, Shot.Image.PixelWidth, Shot.Image.PixelHeight));
        ShotRenderer.DrawAnnotations(dc, Shot, state, ShotIndex, forEditor: true, HiddenCommentIndex);

        if (state.Crop is { } crop) DrawCropDim(dc, crop);

        if (DragRect is { } drag)
        {
            var pen = new Pen(new SolidColorBrush(PreviewColor), 2 * Shot.UiScale);
            pen.Freeze();
            dc.DrawRectangle(PreviewFill, pen, new Rect(drag.X, drag.Y, drag.Width, drag.Height));
        }

        if (BrushHover is { } hover)
        {
            var radius = BrushThickness / 2.0;
            var pen = new Pen(Brushes.White, 1.5 * Shot.UiScale);
            pen.Freeze();
            dc.DrawEllipse(PreviewFill, pen, new Point(hover.X, hover.Y), radius, radius);
        }
    }

    private void DrawCropDim(DrawingContext dc, PixelRect crop)
    {
        double w = Shot!.Image.PixelWidth, h = Shot.Image.PixelHeight;
        dc.DrawRectangle(CropDim, null, new Rect(0, 0, w, crop.Y));
        dc.DrawRectangle(CropDim, null, new Rect(0, crop.Bottom, w, Math.Max(0, h - crop.Bottom)));
        dc.DrawRectangle(CropDim, null, new Rect(0, crop.Y, crop.X, crop.Height));
        dc.DrawRectangle(CropDim, null, new Rect(crop.Right, crop.Y, Math.Max(0, w - crop.Right), crop.Height));
    }

    private static Brush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
