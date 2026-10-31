using System.Collections.Immutable;
using ScreenChick.Model;
using Xunit;

namespace ScreenChick.Tests;

public class ModelTests
{
    private static ShotState WithComments(params string[] comments)
    {
        var state = ShotState.Empty;
        for (var i = 0; i < comments.Length; i++)
        {
            state = state.AddMark(new PixelRect(i * 10, 0, 5, 5)).WithComment(i, comments[i]);
        }
        return state;
    }

    [Fact]
    public void Message_uses_the_same_labels_as_the_images()
    {
        var text = MessageFormatter.Format([WithComments("Кнопку ярче", "Цену больше"), WithComments("Убрать блок")]);

        Assert.Equal("Снимок A\r\nA1 — Кнопку ярче\r\nA2 — Цену больше\r\n\r\nСнимок B\r\nB1 — Убрать блок", text);
    }

    [Fact]
    public void Message_keeps_numbering_when_a_mark_has_no_comment()
    {
        Assert.Equal("Снимок A\r\nA2 — Вторая", MessageFormatter.Format([WithComments("", "Вторая")]));
    }

    [Fact]
    public void Message_skips_shots_without_comments_but_keeps_their_letters()
    {
        Assert.Equal("Снимок B\r\nB1 — Текст", MessageFormatter.Format([ShotState.Empty, WithComments("Текст")]));
    }

    [Fact]
    public void Message_is_empty_without_comments()
    {
        Assert.Equal("", MessageFormatter.Format([ShotState.Empty, WithComments("  ")]));
    }

    [Theory]
    [InlineData(1, "1 снимок")]
    [InlineData(2, "2 снимка")]
    [InlineData(5, "5 снимков")]
    [InlineData(11, "11 снимков")]
    [InlineData(21, "21 снимок")]
    [InlineData(26, "26 снимков")]
    public void Plural_follows_russian_rules(int n, string expected)
    {
        Assert.Equal(expected, MessageFormatter.Plural(n, "снимок", "снимка", "снимков"));
    }

    [Fact]
    public void Crop_drops_marks_outside_and_keeps_those_touching_it()
    {
        var state = ShotState.Empty
            .AddMark(new PixelRect(0, 0, 10, 10))
            .AddMark(new PixelRect(45, 45, 20, 20))
            .AddMark(new PixelRect(200, 200, 10, 10));

        var cropped = state.ApplyCrop(new PixelRect(50, 50, 100, 100));

        Assert.Equal(new PixelRect(45, 45, 20, 20), Assert.Single(cropped.Marks).Area);
    }

    [Fact]
    public void Undo_and_redo_walk_any_number_of_steps()
    {
        var history = new UndoStack<int>(0);
        for (var i = 1; i <= 100; i++) history.Apply(i);

        for (var i = 0; i < 100; i++) history.Undo();
        Assert.Equal(0, history.Current);
        Assert.False(history.CanUndo);

        for (var i = 0; i < 100; i++) history.Redo();
        Assert.Equal(100, history.Current);
    }

    [Fact]
    public void A_new_edit_after_undo_clears_redo()
    {
        var history = new UndoStack<int>(0);
        history.Apply(1);
        history.Undo();
        history.Apply(2);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_mark_and_its_comment_undo_as_one_step()
    {
        var history = new UndoStack<ShotState>(ShotState.Empty);
        history.Apply(history.Current.AddMark(new PixelRect(0, 0, 5, 5)));
        history.ReplaceCurrent(history.Current.WithComment(0, "текст"));

        history.Undo();

        Assert.Empty(history.Current.Marks);
    }

    [Fact]
    public void Dropping_a_step_leaves_nothing_to_redo()
    {
        var history = new UndoStack<int>(0);
        history.Apply(1);
        history.DropCurrent();

        Assert.Equal(0, history.Current);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Clamp_keeps_a_rect_inside_bounds()
    {
        var bounds = new PixelRect(10, 10, 100, 100);

        Assert.Equal(new PixelRect(10, 10, 40, 40), new PixelRect(0, 0, 50, 50).ClampTo(bounds));
        Assert.Equal(new PixelRect(100, 100, 10, 10), new PixelRect(100, 100, 50, 50).ClampTo(bounds));
    }

    [Fact]
    public void Redaction_is_added_without_touching_marks()
    {
        var state = WithComments("a").AddRedaction(new RedactionStroke([new PixelPoint(1, 1)], 10));

        Assert.Single(state.Redactions);
        Assert.Single(state.Marks);
    }
}
