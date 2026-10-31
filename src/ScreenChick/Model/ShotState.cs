using System.Collections.Immutable;

namespace ScreenChick.Model;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsPoint => Width == 0 && Height == 0;

    public static PixelRect FromCorners(PixelPoint a, PixelPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool Contains(PixelPoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public bool Intersects(PixelRect other) =>
        X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    public PixelRect ClampTo(PixelRect bounds)
    {
        var left = Math.Clamp(X, bounds.X, bounds.Right);
        var top = Math.Clamp(Y, bounds.Y, bounds.Bottom);
        var right = Math.Clamp(Right, bounds.X, bounds.Right);
        var bottom = Math.Clamp(Bottom, bounds.Y, bounds.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelPoint Clamp(PixelPoint p) =>
        new(Math.Clamp(p.X, X, Right), Math.Clamp(p.Y, Y, Bottom));
}

/// <summary>A numbered place on a shot. A zero-size area is a pin: a click rather than a drag.</summary>
public sealed record Mark(PixelRect Area, string Comment);

public sealed record RedactionStroke(ImmutableArray<PixelPoint> Points, int Thickness);

/// <summary>Everything the user has done to a shot. Immutable, so undo is a stack of these.</summary>
public sealed record ShotState(
    ImmutableList<Mark> Marks,
    ImmutableList<RedactionStroke> Redactions,
    PixelRect? Crop)
{
    public static readonly ShotState Empty =
        new(ImmutableList<Mark>.Empty, ImmutableList<RedactionStroke>.Empty, null);

    public int CommentCount => Marks.Count(m => !string.IsNullOrWhiteSpace(m.Comment));

    public ShotState AddMark(PixelRect area) => this with { Marks = Marks.Add(new Mark(area, "")) };

    public ShotState WithComment(int index, string comment) =>
        this with { Marks = Marks.SetItem(index, Marks[index] with { Comment = comment }) };

    public ShotState RemoveMark(int index) => this with { Marks = Marks.RemoveAt(index) };

    public ShotState AddRedaction(RedactionStroke stroke) =>
        this with { Redactions = Redactions.Add(stroke) };

    /// <summary>Marks left entirely outside the crop would point at nothing, so they go with it.</summary>
    public ShotState ApplyCrop(PixelRect crop) => this with
    {
        Crop = crop,
        Marks = Marks.RemoveAll(m => !m.Area.Intersects(crop)),
    };
}
