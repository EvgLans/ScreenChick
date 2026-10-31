using System.Globalization;
using System.Reflection;
using ScreenChick.Localization;
using ScreenChick.Model;
using Xunit;

// The language is global state, and each test may read or switch it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ScreenChick.Tests;

public class LocalizationTests
{
    private static List<(string Name, string Text)> AllTexts()
    {
        var texts = new List<(string, string)>();
        foreach (var property in typeof(L).GetProperties(BindingFlags.Public | BindingFlags.Static))
            texts.Add((property.Name, (string)property.GetValue(null)!));

        texts.Add((nameof(L.Shots), L.Shots(2)));
        texts.Add((nameof(L.Comments), L.Comments(2)));
        texts.Add((nameof(L.StartedBody), L.StartedBody("F10")));
        texts.Add((nameof(L.StripFull), L.StripFull(26)));
        texts.Add((nameof(L.Milliseconds), L.Milliseconds(400)));
        return texts;
    }

    private static void InLanguage(UiLanguage language, Action action)
    {
        L.Use(language);
        try { action(); }
        finally { L.Use(UiLanguage.Russian); }
    }

    private static bool HasCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    [Fact]
    public void English_has_no_russian_left() => InLanguage(UiLanguage.English, () =>
    {
        foreach (var (name, text) in AllTexts())
        {
            Assert.False(string.IsNullOrWhiteSpace(text), name);
            Assert.False(HasCyrillic(text), $"{name}: {text}");
        }
    });

    [Fact]
    public void Russian_texts_are_really_russian() => InLanguage(UiLanguage.Russian, () =>
    {
        foreach (var (name, text) in AllTexts()) Assert.True(HasCyrillic(text), $"{name}: {text}");
    });

    [Theory]
    [InlineData(UiLanguage.Auto, "ru-RU", true)]
    [InlineData(UiLanguage.Auto, "en-US", false)]
    [InlineData(UiLanguage.Auto, "de-DE", false)]
    [InlineData(UiLanguage.Russian, "en-US", true)]
    [InlineData(UiLanguage.English, "ru-RU", false)]
    public void Language_follows_the_setting_then_the_system(UiLanguage language, string system, bool russian) =>
        Assert.Equal(russian, L.IsRussian(language, new CultureInfo(system)));

    [Theory]
    [InlineData(1, "1 shot", "1 comment")]
    [InlineData(2, "2 shots", "2 comments")]
    [InlineData(21, "21 shots", "21 comments")]
    public void English_counts_read_correctly(int n, string shots, string comments) => InLanguage(UiLanguage.English, () =>
    {
        Assert.Equal(shots, L.Shots(n));
        Assert.Equal(comments, L.Comments(n));
    });

    [Fact]
    public void Pasted_text_uses_the_interface_language() => InLanguage(UiLanguage.English, () =>
    {
        var state = ShotState.Empty.AddMark(new PixelRect(1, 1, 5, 5)).WithComment(0, "Make it brighter");

        Assert.Equal("Shot A\r\nA1 — Make it brighter", MessageFormatter.Format([state]));
    });

    [Fact]
    public void Switching_reports_whether_windows_need_recreating() => InLanguage(UiLanguage.Russian, () =>
    {
        Assert.True(L.Use(UiLanguage.English));
        Assert.False(L.Use(UiLanguage.English));
        Assert.True(L.Use(UiLanguage.Russian));
    });
}
