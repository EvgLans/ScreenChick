using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenChick.Model;

namespace ScreenChick.Rendering;

/// <summary>
/// Draws annotations with one code path for the editor and the final image,
/// so what you see on the frozen screen is exactly what gets pasted.
/// </summary>
public static class ShotRenderer
{
    private const double BadgeFontSize = 13;
    private const double CommentMaxWidth = 320;
    private const double LetterMargin = 4;

    private static readonly Typeface BadgeFace =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface CommentFace = new("Segoe UI");

    // The duckling's colors: orange for outlines, which have to show on any page, yellow for the badges.
    private static readonly Brush Accent = Frozen(Color.FromRgb(0xF4, 0x51, 0x1E));
    private static readonly Brush MarkBackground = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly Brush MarkForeground = Frozen(Color.FromRgb(0x20, 0x21, 0x24));
    private static readonly Brush RedactionBrush = Frozen(Color.FromRgb(0x20, 0x21, 0x24));
    private static readonly Brush CommentBackground = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush CommentForeground = Frozen(Color.FromRgb(0x20, 0x21, 0x24));
    private static readonly Brush LetterBackground = Frozen(Color.FromRgb(0x20, 0x21, 0x24));
    private static readonly Brush LetterForeground = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));

    public static void DrawAnnotations(
        DrawingContext dc, Shot shot, ShotState state, int shotIndex, bool forEditor, int hiddenCommentIndex = -1)
    {
        var s = shot.UiScale;
        var visible = state.Crop ?? shot.FullBounds;

        foreach (var stroke in state.Redactions) DrawRedaction(dc, stroke);

        var outline = new Pen(Accent, 2 * s);
        outline.Freeze();
        var badgeEdge = new Pen(Accent, 1 * s);
        badgeEdge.Freeze();

        for (var i = 0; i < state.Marks.Count; i++)
        {
            var mark = state.Marks[i];
            if (!mark.Area.IsPoint)
            {
                var a = mark.Area.ClampTo(visible);
                dc.DrawRoundedRectangle(null, outline, new Rect(a.X, a.Y, a.Width, a.Height), 3 * s, 3 * s);
            }

            var badge = BadgeRect(state, shotIndex, i, s, visible);
            DrawBadge(dc, ShotQueue.LabelOf(shotIndex, i), badge, MarkBackground, MarkForeground, badgeEdge, s);

            if (forEditor && i != hiddenCommentIndex && !string.IsNullOrWhiteSpace(mark.Comment))
                DrawCommentBubble(dc, mark.Comment, badge, s, visible);
        }

        DrawBadge(dc, ShotQueue.LetterOf(shotIndex).ToString(), LetterRect(state, shotIndex, s, visible), LetterBackground, LetterForeground, null, s);
    }

    /// <summary>Flattens a shot into the pixels that leave the app. Nothing under a redaction survives.</summary>
    public static BitmapSource RenderFinal(Shot shot, int shotIndex)
    {
        var state = shot.State;
        var visible = state.Crop ?? shot.FullBounds;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-visible.X, -visible.Y));
            dc.DrawImage(shot.Image, new Rect(0, 0, shot.Image.PixelWidth, shot.Image.PixelHeight));
            DrawAnnotations(dc, shot, state, shotIndex, forEditor: false);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, visible.Width), Math.Max(1, visible.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource RenderThumbnail(Shot shot, int shotIndex, int maxWidth, int maxHeight)
    {
        var final = RenderFinal(shot, shotIndex);
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / final.PixelWidth, (double)maxHeight / final.PixelHeight));
        var thumbnail = new TransformedBitmap(final, new ScaleTransform(scale, scale));
        thumbnail.Freeze();
        return thumbnail;
    }

    /// <summary>The mark under a point: badges first, then the smallest area, so nested marks stay reachable.</summary>
    public static int HitTest(ShotState state, int shotIndex, double uiScale, PixelRect visible, Point p)
    {
        for (var i = state.Marks.Count - 1; i >= 0; i--)
        {
            if (BadgeRect(state, shotIndex, i, uiScale, visible).Contains(p)) return i;
        }

        var hit = -1;
        var hitArea = long.MaxValue;
        var point = new PixelPoint((int)p.X, (int)p.Y);
        for (var i = 0; i < state.Marks.Count; i++)
        {
            var area = state.Marks[i].Area;
            var size = (long)area.Width * area.Height;
            if (!area.IsPoint && area.Contains(point) && size < hitArea)
            {
                hit = i;
                hitArea = size;
            }
        }
        return hit;
    }

    public static Rect BadgeRect(ShotState state, int shotIndex, int markIndex, double uiScale, PixelRect visible)
    {
        var mark = state.Marks[markIndex];
        var size = MeasureBadge(ShotQueue.LabelOf(shotIndex, markIndex), uiScale);
        var letter = LetterRect(state, shotIndex, uiScale, visible);
        var margin = LetterMargin * uiScale;
        var position = mark.Area.IsPoint
            ? PinBadgePosition(mark.Area, size, letter, margin, visible)
            : AreaBadgePosition(mark.Area, size, letter, margin, visible);
        return new Rect(position, size);
    }

    /// <summary>
    /// The shot's letter takes the first corner clear of the selected areas. Only an area
    /// reaching every corner, such as one selecting the whole crop, can leave it on top of a selection.
    /// </summary>
    public static Rect LetterRect(ShotState state, int shotIndex, double uiScale, PixelRect visible)
    {
        var size = MeasureBadge(ShotQueue.LetterOf(shotIndex).ToString(), uiScale);
        var margin = LetterMargin * uiScale;
        var left = visible.X + margin;
        var top = visible.Y + margin;
        var right = Math.Max(left, visible.Right - margin - size.Width);
        var bottom = Math.Max(top, visible.Bottom - margin - size.Height);

        var areas = state.Marks.Where(m => !m.Area.IsPoint).Select(m => ToRect(m.Area.ClampTo(visible))).ToList();
        Point[] corners = [new(left, top), new(right, top), new(left, bottom), new(right, bottom)];
        var corner = corners.OrderBy(c => areas.Sum(a => OverlapArea(new Rect(c, size), a))).First();
        return new Rect(corner, size);
    }

    /// <summary>A pin's badge sits on the click, moved off the letter by a fixed offset so nearby pins stay apart.</summary>
    private static Point PinBadgePosition(PixelRect pin, Size size, Rect letter, double margin, PixelRect visible)
    {
        var x = ClampX(pin.X - size.Width / 2, size, visible);
        var y = ClampY(pin.Y - size.Height / 2, size, visible);
        if (OverlapArea(new Rect(x, y, size.Width, size.Height), letter) == 0) return new Point(x, y);

        var awayX = letter.X + letter.Width / 2 < visible.X + visible.Width / 2.0 ? 1 : -1;
        var awayY = letter.Y + letter.Height / 2 < visible.Y + visible.Height / 2.0 ? 1 : -1;
        return new Point(
            ClampX(x + awayX * (letter.Width + 2 * margin), size, visible),
            ClampY(y + awayY * (letter.Height + 2 * margin), size, visible));
    }

    /// <summary>
    /// An area's badge sits above its top-left corner when it can, otherwise at the nearest spot
    /// clear of the area and the letter. Only an area leaving no room for the badge can end up under it.
    /// </summary>
    private static Point AreaBadgePosition(PixelRect mark, Size size, Rect letter, double margin, PixelRect visible)
    {
        var area = mark.ClampTo(visible);
        var target = ToRect(area);
        var preferred = new Point(area.X, area.Y - size.Height);

        double[] xs = [area.X, area.Right, area.X - size.Width, letter.Right + margin, letter.X - margin - size.Width, visible.X, visible.Right];
        double[] ys = [area.Y - size.Height, area.Bottom, area.Y, letter.Bottom + margin, letter.Y - margin - size.Height, visible.Y, visible.Bottom];

        return xs
            .SelectMany(_ => ys, (x, y) => new Point(ClampX(x, size, visible), ClampY(y, size, visible)))
            .OrderBy(p => OverlapArea(new Rect(p, size), target))
            .ThenBy(p => OverlapArea(new Rect(p, size), letter))
            .ThenBy(p => (p - preferred).LengthSquared)
            .First();
    }

    private static double ClampX(double x, Size size, PixelRect visible) =>
        Math.Clamp(x, visible.X, Math.Max(visible.X, visible.Right - size.Width));

    private static double ClampY(double y, Size size, PixelRect visible) =>
        Math.Clamp(y, visible.Y, Math.Max(visible.Y, visible.Bottom - size.Height));

    private static Rect ToRect(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>Rounded to whole pixels, so badges that only touch an edge count as clear despite floating point.</summary>
    private static double OverlapArea(Rect a, Rect b)
    {
        var overlap = Rect.Intersect(a, b);
        return overlap.IsEmpty ? 0 : Math.Round(overlap.Width * overlap.Height);
    }

    private static void DrawRedaction(DrawingContext dc, RedactionStroke stroke)
    {
        if (stroke.Points.Length == 0) return;

        var first = stroke.Points[0];
        if (stroke.Points.Length == 1)
        {
            var radius = stroke.Thickness / 2.0;
            dc.DrawEllipse(RedactionBrush, null, new Point(first.X, first.Y), radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(first.X, first.Y), false, false);
            for (var i = 1; i < stroke.Points.Length; i++)
                ctx.LineTo(new Point(stroke.Points[i].X, stroke.Points[i].Y), true, true);
        }
        geometry.Freeze();

        var pen = new Pen(RedactionBrush, stroke.Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Size MeasureBadge(string label, double s)
    {
        var text = BadgeText(label, s, MarkForeground);
        return new Size(text.WidthIncludingTrailingWhitespace + 12 * s, text.Height + 4 * s);
    }

    private static void DrawBadge(DrawingContext dc, string label, Rect rect, Brush background, Brush foreground, Pen? edge, double s)
    {
        dc.DrawRoundedRectangle(background, edge, rect, 4 * s, 4 * s);
        dc.DrawText(BadgeText(label, s, foreground), new Point(rect.X + 6 * s, rect.Y + 2 * s));
    }

    private static void DrawCommentBubble(DrawingContext dc, string comment, Rect badge, double s, PixelRect visible)
    {
        var text = new FormattedText(
            comment, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CommentFace,
            BadgeFontSize * s, CommentForeground, 1.0)
        {
            MaxTextWidth = CommentMaxWidth * s,
            MaxLineCount = 4,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var width = text.WidthIncludingTrailingWhitespace + 12 * s;
        var height = text.Height + 6 * s;
        var x = badge.Right + 4 * s;
        if (x + width > visible.Right) x = Math.Max(visible.X, badge.X - width - 4 * s);
        var y = Math.Clamp(badge.Y, visible.Y, Math.Max(visible.Y, visible.Bottom - height));

        // The outline keeps a white bubble readable on a white page.
        var edge = new Pen(Accent, 1 * s);
        edge.Freeze();
        dc.DrawRoundedRectangle(CommentBackground, edge, new Rect(x, y, width, height), 4 * s, 4 * s);
        dc.DrawText(text, new Point(x + 6 * s, y + 3 * s));
    }

    private static FormattedText BadgeText(string label, double s, Brush foreground) => new(
        label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeFace,
        BadgeFontSize * s, foreground, 1.0);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
