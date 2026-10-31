using System.Text;

namespace ScreenChick.Model;

public static class MessageFormatter
{
    /// <summary>Builds the text pasted under the images. Empty when there are no comments at all.</summary>
    public static string Format(IReadOnlyList<ShotState> shots)
    {
        var text = new StringBuilder();
        for (var s = 0; s < shots.Count; s++)
        {
            var marks = shots[s].Marks;
            if (shots[s].CommentCount == 0) continue;

            if (text.Length > 0) text.Append("\r\n\r\n");
            text.Append("Снимок ").Append(ShotQueue.LetterOf(s));
            for (var m = 0; m < marks.Count; m++)
            {
                if (string.IsNullOrWhiteSpace(marks[m].Comment)) continue;
                text.Append("\r\n").Append(ShotQueue.LabelOf(s, m)).Append(" — ").Append(marks[m].Comment.Trim());
            }
        }
        return text.ToString();
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var tens = n % 100;
        var units = n % 10;
        var word = tens is >= 11 and <= 14 ? many
            : units == 1 ? one
            : units is >= 2 and <= 4 ? few
            : many;
        return $"{n} {word}";
    }
}
