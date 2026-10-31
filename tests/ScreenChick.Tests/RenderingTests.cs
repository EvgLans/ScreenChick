using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenChick.Localization;
using ScreenChick.Model;
using ScreenChick.Paste;
using ScreenChick.Rendering;
using ScreenChick.UI;
using Xunit;

namespace ScreenChick.Tests;

public class RenderingTests
{
    private static readonly Color Background = Color.FromRgb(200, 220, 240);

    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color PixelAt(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    [Fact]
    public void Final_image_is_exactly_the_cropped_size() => OnSta(() =>
    {
        var shot = new Shot(Solid(400, 300, Background), 1.0);
        shot.History.Apply(shot.State.ApplyCrop(new PixelRect(50, 40, 120, 90)));

        var final = ShotRenderer.RenderFinal(shot, 0);

        Assert.Equal(120, final.PixelWidth);
        Assert.Equal(90, final.PixelHeight);
    });

    [Fact]
    public void Untouched_pixels_come_through_unchanged() => OnSta(() =>
    {
        var final = ShotRenderer.RenderFinal(new Shot(Solid(400, 300, Background), 1.0), 0);

        Assert.Equal(Background, PixelAt(final, 200, 150));
    });

    [Fact]
    public void Redaction_destroys_the_pixels_underneath() => OnSta(() =>
    {
        var secret = Color.FromRgb(10, 200, 30);
        var shot = new Shot(Solid(400, 300, secret), 1.0);
        var stroke = new RedactionStroke([new PixelPoint(50, 150), new PixelPoint(350, 150)], 40);
        shot.History.Apply(shot.State.AddRedaction(stroke));

        var final = ShotRenderer.RenderFinal(shot, 0);

        var covered = PixelAt(final, 200, 150);
        Assert.NotEqual(secret, covered);
        Assert.Equal(Color.FromRgb(0x20, 0x21, 0x24), covered);
        Assert.Equal(secret, PixelAt(final, 200, 250));
    });

    [Fact]
    public void A_single_click_redaction_still_covers_a_spot() => OnSta(() =>
    {
        var shot = new Shot(Solid(100, 100, Background), 1.0);
        shot.History.Apply(shot.State.AddRedaction(new RedactionStroke([new PixelPoint(50, 50)], 20)));

        Assert.Equal(Color.FromRgb(0x20, 0x21, 0x24), PixelAt(ShotRenderer.RenderFinal(shot, 0), 50, 50));
    });

    [Fact]
    public void Mark_outline_lands_at_its_position_inside_a_crop() => OnSta(() =>
    {
        var shot = new Shot(Solid(400, 300, Background), 1.0);
        shot.History.Apply(shot.State.AddMark(new PixelRect(100, 100, 100, 100)));
        shot.History.Apply(shot.State.ApplyCrop(new PixelRect(50, 50, 300, 200)));

        var final = ShotRenderer.RenderFinal(shot, 0);

        // The crop starts at (50, 50), so the mark's left edge moves from x=100 to x=50.
        Assert.NotEqual(Background, PixelAt(final, 50, 100));
        Assert.Equal(Background, PixelAt(final, 100, 100));
    });

    [Fact]
    public void Clicking_a_nested_mark_picks_the_smaller_one() => OnSta(() =>
    {
        var state = ShotState.Empty
            .AddMark(new PixelRect(10, 100, 300, 300))
            .AddMark(new PixelRect(100, 200, 50, 50));

        var hit = ShotRenderer.HitTest(state, 0, 1.0, new PixelRect(0, 0, 500, 500), new Point(120, 220));

        Assert.Equal(1, hit);
    });

    [Fact]
    public void Clicking_empty_space_hits_nothing() => OnSta(() =>
    {
        var state = ShotState.Empty.AddMark(new PixelRect(10, 100, 30, 30));

        Assert.Equal(-1, ShotRenderer.HitTest(state, 0, 1.0, new PixelRect(0, 0, 500, 500), new Point(400, 400)));
    });

    private static ShotState WithMark(int x, int y, int width, int height) =>
        ShotState.Empty.AddMark(new PixelRect(x, y, width, height));

    private static bool Overlaps(Rect a, Rect b)
    {
        var overlap = Rect.Intersect(a, b);
        return !overlap.IsEmpty && overlap.Width * overlap.Height >= 1;
    }

    private static bool Inside(Rect r, PixelRect bounds) =>
        r.Left >= bounds.X && r.Top >= bounds.Y && r.Right <= bounds.Right && r.Bottom <= bounds.Bottom;

    [Fact]
    public void Badge_stays_inside_the_visible_area_for_a_mark_at_the_top_edge() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);

        var badge = ShotRenderer.BadgeRect(WithMark(490, 0, 10, 10), 0, 0, 1.0, visible);

        Assert.True(badge.Top >= 0);
        Assert.True(badge.Right <= 500);
    });

    [Theory]
    [InlineData(0, 0)]      // a click in the corner
    [InlineData(10, 10)]    // a click near the corner
    public void Pin_badge_moves_right_and_down_off_the_shot_letter(int x, int y) => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);
        var state = WithMark(x, y, 0, 0);

        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, visible);
        var letter = ShotRenderer.LetterRect(state, 0, 1.0, visible);

        Assert.False(badge.IntersectsWith(letter));
        Assert.True(badge.Left >= letter.Right);
        Assert.True(badge.Top >= letter.Bottom);
    });

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Area_badge_is_never_on_its_area_or_on_the_letter(double scale) => OnSta(() =>
    {
        PixelRect[] views = [new(0, 0, 800, 600), new(150, 90, 500, 400)];
        int[] sizes = [3, 12, 40, 150, 300];

        foreach (var view in views)
        foreach (var width in sizes)
        foreach (var height in sizes)
        for (var x = view.X; x <= view.Right - width; x += Math.Max(1, (view.Width - width) / 6))
        for (var y = view.Y; y <= view.Bottom - height; y += Math.Max(1, (view.Height - height) / 6))
        {
            var state = WithMark(x, y, width, height);
            var area = new Rect(x, y, width, height);

            var badge = ShotRenderer.BadgeRect(state, 0, 0, scale, view);
            var letter = ShotRenderer.LetterRect(state, 0, scale, view);

            var where = $"area ({x},{y},{width},{height}) in {view}, badge {badge}, letter {letter}";
            Assert.False(Overlaps(badge, area), where);
            Assert.False(Overlaps(letter, area), where);
            Assert.False(Overlaps(badge, letter), where);
            Assert.True(Inside(badge, view), where);
        }
    });

    [Fact]
    public void Area_badge_clears_the_part_of_the_area_left_after_a_crop() => OnSta(() =>
    {
        var crop = new PixelRect(200, 150, 300, 200);
        var state = WithMark(100, 100, 150, 120).ApplyCrop(crop);
        var kept = new Rect(200, 150, 50, 70);

        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, crop);

        Assert.False(Overlaps(badge, kept));
        Assert.True(Inside(badge, crop));
    });

    [Fact]
    public void Area_badge_with_room_above_stays_on_the_top_left() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);

        var badge = ShotRenderer.BadgeRect(WithMark(100, 100, 50, 50), 0, 0, 1.0, visible);

        Assert.Equal(100, badge.Left);
        Assert.Equal(100 - badge.Height, badge.Top, 6);
    });

    [Fact]
    public void Area_filling_the_view_keeps_the_badge_inside_and_off_the_letter() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);
        var state = WithMark(0, 0, 500, 500);

        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, visible);

        Assert.True(Inside(badge, visible));
        Assert.False(Overlaps(badge, ShotRenderer.LetterRect(state, 0, 1.0, visible)));
    });

    [Fact]
    public void Letter_stays_top_left_while_that_corner_is_free() => OnSta(() =>
    {
        var visible = new PixelRect(100, 50, 400, 300);

        var letter = ShotRenderer.LetterRect(WithMark(250, 150, 50, 50), 0, 1.0, visible);

        Assert.Equal(104, letter.X);
        Assert.Equal(54, letter.Y);
    });

    [Fact]
    public void Letter_moves_to_the_next_free_corner_when_areas_cover_it() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 400);

        var topLeftTaken = ShotRenderer.LetterRect(WithMark(0, 0, 100, 100), 0, 1.0, visible);
        Assert.True(topLeftTaken.Right > 400 && topLeftTaken.Top < 100);

        var topTaken = ShotRenderer.LetterRect(
            WithMark(0, 0, 100, 100).AddMark(new PixelRect(400, 0, 100, 100)), 0, 1.0, visible);
        Assert.True(topTaken.Left < 100 && topTaken.Bottom > 300);
    });

    [Fact]
    public void Pin_badge_clears_a_letter_moved_to_another_corner() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 400);
        var state = WithMark(0, 0, 100, 100).AddMark(new PixelRect(495, 5, 0, 0));

        var badge = ShotRenderer.BadgeRect(state, 0, 1, 1.0, visible);

        Assert.False(badge.IntersectsWith(ShotRenderer.LetterRect(state, 0, 1.0, visible)));
        Assert.True(Inside(badge, visible));
    });

    [Fact]
    public void Pin_badge_away_from_the_letter_stays_centered_on_the_click() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);

        var badge = ShotRenderer.BadgeRect(WithMark(200, 200, 0, 0), 0, 0, 1.0, visible);

        Assert.Equal(200, badge.X + badge.Width / 2, 6);
        Assert.Equal(200, badge.Y + badge.Height / 2, 6);
    });

    [Fact]
    public void Corner_pins_that_were_apart_stay_apart_after_moving() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);
        var state = WithMark(20, 15, 0, 0).AddMark(new PixelRect(30, 25, 0, 0));

        var first = ShotRenderer.BadgeRect(state, 0, 0, 1.0, visible);
        var second = ShotRenderer.BadgeRect(state, 0, 1, 1.0, visible);

        Assert.Equal(10, second.Left - first.Left, 3);
        Assert.Equal(10, second.Top - first.Top, 3);
    });

    [Theory]
    [InlineData(40, 200)]    // too narrow to move right, so it only moves down
    [InlineData(200, 30)]    // too short to move down, so it only moves right
    public void In_a_tight_crop_the_badge_still_clears_the_letter_along_the_free_axis(int width, int height) => OnSta(() =>
    {
        var crop = new PixelRect(100, 100, width, height);
        var state = WithMark(100, 100, 0, 0);

        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, crop);

        Assert.False(badge.IntersectsWith(ShotRenderer.LetterRect(state, 0, 1.0, crop)));
        Assert.True(badge.Right <= crop.Right && badge.Bottom <= crop.Bottom);
    });

    [Fact]
    public void Badge_in_a_crop_too_small_for_both_stays_inside_it() => OnSta(() =>
    {
        var crop = new PixelRect(100, 100, 30, 25);

        var badge = ShotRenderer.BadgeRect(WithMark(100, 100, 0, 0), 0, 0, 1.0, crop);

        Assert.True(badge.Left >= crop.X && badge.Top >= crop.Y);
    });

    [Fact]
    public void Final_image_shows_the_letter_and_the_mark_side_by_side_in_a_cropped_corner() => OnSta(() =>
    {
        var shot = new Shot(Solid(400, 300, Background), 1.0);
        var crop = new PixelRect(50, 40, 200, 150);
        shot.History.Apply(shot.State.AddMark(new PixelRect(50, 40, 0, 0)));
        shot.History.Apply(shot.State.ApplyCrop(crop));

        var final = ShotRenderer.RenderFinal(shot, 0);
        var letter = ShotRenderer.LetterRect(shot.State, 0, 1.0, crop);
        var badge = ShotRenderer.BadgeRect(shot.State, 0, 0, 1.0, crop);

        // The left padding of each badge holds no text, so it shows the badge's own color.
        Assert.Equal(Color.FromRgb(0x20, 0x21, 0x24),
            PixelAt(final, (int)letter.X - crop.X + 3, (int)(letter.Y + letter.Height / 2) - crop.Y));
        Assert.Equal(Color.FromRgb(0xFF, 0xC4, 0x00),
            PixelAt(final, (int)badge.X - crop.X + 3, (int)(badge.Y + badge.Height / 2) - crop.Y));
    });

    [Theory]
    [InlineData(50, 40, 100, 80)]     // the crop's top-left corner
    [InlineData(50, 40, 12, 10)]      // a selection smaller than the badge, in that corner
    [InlineData(120, 40, 60, 30)]     // along the crop's top edge
    [InlineData(190, 150, 60, 40)]    // the crop's bottom-right corner
    [InlineData(55, 60, 40, 40)]      // just under where the letter normally sits
    public void Final_image_leaves_every_pixel_inside_the_selection_visible(int x, int y, int width, int height) => OnSta(() =>
    {
        var shot = new Shot(Solid(400, 300, Background), 1.0);
        var crop = new PixelRect(50, 40, 200, 150);
        shot.History.Apply(shot.State.ApplyCrop(crop));
        shot.History.Apply(shot.State.AddMark(new PixelRect(x, y, width, height)));

        var final = ShotRenderer.RenderFinal(shot, 0);

        // Inset past the outline, which is the only thing allowed inside.
        for (var py = y + 3; py < y + height - 3; py++)
        for (var px = x + 3; px < x + width - 3; px++)
            Assert.Equal(Background, PixelAt(final, px - crop.X, py - crop.Y));
    });

    [Fact]
    public void Clicking_a_moved_corner_badge_finds_its_mark() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);
        var state = WithMark(0, 0, 0, 0);
        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, visible);

        var hit = ShotRenderer.HitTest(state, 0, 1.0, visible, new Point(badge.X + badge.Width / 2, badge.Y + badge.Height / 2));

        Assert.Equal(0, hit);
    });

    [Fact]
    public void Clicking_the_badge_of_a_corner_area_finds_its_mark() => OnSta(() =>
    {
        var visible = new PixelRect(0, 0, 500, 500);
        var state = WithMark(0, 0, 40, 40);
        var badge = ShotRenderer.BadgeRect(state, 0, 0, 1.0, visible);

        var hit = ShotRenderer.HitTest(state, 0, 1.0, visible, new Point(badge.X + badge.Width / 2, badge.Y + badge.Height / 2));

        Assert.Equal(0, hit);
    });

    [Fact]
    public void Dib_header_and_row_order_match_cf_dib() => OnSta(() =>
    {
        // Width 3 makes rows 9 bytes, padded to 12, which is where stride bugs show up.
        const int width = 3, height = 2;
        byte[] pixels =
        [
            1, 2, 3, 255,  1, 2, 3, 255,  1, 2, 3, 255,
            9, 8, 7, 255,  9, 8, 7, 255,  9, 8, 7, 255,
        ];
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);

        var dib = ClipboardPayload.EncodeDib(image);

        Assert.Equal(40 + 12 * height, dib.Length);
        Assert.Equal(40, BitConverter.ToInt32(dib, 0));
        Assert.Equal(width, BitConverter.ToInt32(dib, 4));
        Assert.Equal(height, BitConverter.ToInt32(dib, 8));
        Assert.Equal(24, BitConverter.ToInt16(dib, 14));

        // Bottom-up: the image's last row is stored first.
        Assert.Equal([9, 8, 7], dib[40..43]);
        Assert.Equal([1, 2, 3], dib[(40 + 12)..(40 + 15)]);
    });

    [Fact]
    public void Png_round_trips_with_same_size() => OnSta(() =>
    {
        var png = ClipboardPayload.EncodePng(Solid(64, 32, Background));

        var decoded = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

        Assert.Equal(64, decoded.PixelWidth);
        Assert.Equal(32, decoded.PixelHeight);
    });

    [Fact]
    public void Settings_window_shows_a_recorded_hotkey_and_ignores_esc() => OnSta(() =>
    {
        var window = new SettingsWindow(new Settings.AppSettings(), new Input.KeyRouter(() => Input.PasteDecision.Pass));

        window.OnHotkeyRecorded(new Input.Hotkey(0x53, Input.KeyMods.Ctrl | Input.KeyMods.Shift));
        Assert.Equal("Ctrl+Shift+S", window.HotkeyText.Text);

        window.OnHotkeyRecorded(new Input.Hotkey(Input.KeyRouter.VkEscape, Input.KeyMods.None));
        Assert.Equal("Ctrl+Shift+S", window.HotkeyText.Text);
    });

    /// <summary>Set SCREENCHICK_PREVIEW_DIR to write PNGs of the UI, rendered offscreen from a synthetic page.</summary>
    [Fact]
    public void Writes_ui_previews_when_asked() => OnSta(() =>
    {
        var dir = Environment.GetEnvironmentVariable("SCREENCHICK_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);

        Save(ShotRenderer.RenderFinal(SampleShot(english: false), 0), Path.Combine(dir, "final.png"));

        PixelRect[] selections =
        [
            new(0, 0, 90, 60), new(0, 0, 14, 12), new(150, 0, 80, 40), new(320, 0, 80, 200),
            new(320, 200, 80, 100), new(0, 240, 60, 60), new(5, 30, 80, 40), new(0, 0, 400, 300),
        ];
        for (var i = 0; i < selections.Length; i++)
        {
            var corner = new Shot(Solid(400, 300, Background), 1.0);
            corner.History.Apply(corner.State.AddMark(selections[i]));
            Save(ShotRenderer.RenderFinal(corner, 0), Path.Combine(dir, $"selection-{i}.png"));
        }

        WriteWindowPreviews(dir, "", english: false);

        L.Use(UiLanguage.English);
        try { WriteWindowPreviews(dir, "-en", english: true); }
        finally { L.Use(UiLanguage.Russian); }
    });

    /// <summary>The pictures in the READMEs. Windows read their text when created, so each language gets its own.</summary>
    private static void WriteWindowPreviews(string dir, string suffix, bool english)
    {
        var shot = SampleShot(english);
        var second = SecondSampleShot(english);

        var overlay = new OverlayWindow(shot, 0, new Capture.PixelBounds(0, 0, 1280, 720));
        Save(RenderElement(overlay.Root, 1280, 720), Path.Combine(dir, $"overlay{suffix}.png"));

        var queue = new ShotQueue();
        queue.Add(shot);
        queue.Add(second);
        var strip = new StripWindow();
        strip.Refresh(queue);
        strip.SetStatus(L.Shots(2) + " · " + L.Comments(4), true);
        SaveWindowContent(strip, Path.Combine(dir, $"strip{suffix}.png"));

        var settings = new SettingsWindow(new Settings.AppSettings(), new Input.KeyRouter(() => Input.PasteDecision.Pass));
        SaveWindowContent(settings, Path.Combine(dir, $"settings{suffix}.png"));

        Save(RenderChatField([shot, second]), Path.Combine(dir, $"chat{suffix}.png"));
    }

    private static void SaveWindowContent(Window window, string path) => Save(RenderWindowContent(window), path);

    private static BitmapSource RenderWindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return RenderElement(content, (int)Math.Ceiling(content.DesiredSize.Width), (int)Math.Ceiling(content.DesiredSize.Height));
    }

    /// <summary>Set SCREENCHICK_ANIMATION_DIR to write the README animation's frames; installer\make-animation.ps1 turns them into GIFs.</summary>
    [Fact]
    public void Writes_animation_frames_when_asked() => OnSta(() =>
    {
        var dir = Environment.GetEnvironmentVariable("SCREENCHICK_ANIMATION_DIR");
        if (string.IsNullOrEmpty(dir)) return;

        L.Use(UiLanguage.Russian);
        WriteAnimationFrames(Path.Combine(dir, "ru"), english: false);

        L.Use(UiLanguage.English);
        try { WriteAnimationFrames(Path.Combine(dir, "en"), english: true); }
        finally { L.Use(UiLanguage.Russian); }
    });

    /// <summary>Capture, two marks, Ctrl+V. The overlay, strip and pasted content are the app's own; the page, chat, pointer and key hints are drawn here.</summary>
    private static void WriteAnimationFrames(string dir, bool english)
    {
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);

        var count = 0;
        BitmapSource? shown = null;

        // Fade frames are marked so the palette is built from the settled frames only.
        void Emit(BitmapSource image, int milliseconds, bool fade = false)
        {
            Save(image, Path.Combine(dir, $"{count++:D4}_{milliseconds}{(fade ? "_f" : "")}.png"));
            shown = image;
        }

        void FadeTo(BitmapSource next, int steps)
        {
            var from = shown!;
            for (var i = 1; i <= steps; i++)
                Emit(BlendFrames(from, next, Smooth((double)i / (steps + 1))), AnimationTick, fade: true);
        }

        void Frame(BitmapSource scene, Point? pointer, int milliseconds, bool cross = false, string? key = null, BitmapSource? strip = null, Rect? caret = null) =>
            Emit(ComposeAnimationFrame(scene, pointer, cross, key, strip, caret), milliseconds);

        // Like Frame, but arrives by fading, so nothing on screen changes abruptly.
        void Soft(BitmapSource scene, Point? pointer, int milliseconds, bool cross = false, string? key = null, BitmapSource? strip = null, int steps = 6)
        {
            var next = ComposeAnimationFrame(scene, pointer, cross, key, strip, null);
            FadeTo(next, steps);
            Emit(next, milliseconds);
        }

        void Move(Func<BitmapSource> scene, Point from, Point to, int steps, bool cross = false, BitmapSource? strip = null)
        {
            for (var i = 1; i <= steps; i++)
                Frame(scene(), Between(from, to, Smooth((double)i / steps)), AnimationTick, cross, strip: strip);
        }

        var opening = default(BitmapSource);
        foreach (var (image, milliseconds) in AnimationIntro(english))
        {
            opening ??= image;
            Emit(image, milliseconds);
        }

        var page = SamplePage(english);
        var rest = new Point(300, 440);
        Soft(page, rest, 500, steps: 10);
        Soft(page, rest, 500, key: "Print Screen");

        var shot = new Shot(page, 1.0);
        var overlay = new OverlayWindow(shot, 0, new Capture.PixelBounds(0, 0, 1280, 720));
        BitmapSource Render() { overlay.Surface.InvalidateVisual(); return RenderElement(overlay.Root, 1280, 720); }
        void Invoke(string method, params object[] args) => typeof(OverlayWindow)
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(overlay, args);

        void Type(string comment, Point pointer)
        {
            // The editor fades in empty, then the text comes one letter at a time.
            overlay.CommentBox.Text = "";
            Soft(Render(), pointer, 120, cross: true);
            for (var length = 1; length <= comment.Length; length++)
            {
                overlay.CommentBox.Text = comment[..length];
                overlay.CommentBox.CaretIndex = length;
                var scene = Render();
                var position = overlay.CommentBox.GetRectFromCharacterIndex(length);
                var caret = overlay.CommentBox.TransformToAncestor(overlay.Root).TransformBounds(position);
                Frame(scene, pointer, length == comment.Length ? 450 : 40, cross: true, caret: new Rect(caret.X, caret.Y, 1.5, caret.Height));
            }
        }

        Soft(Render(), rest, 350, cross: true);

        // First mark: an area dragged around the button.
        var area = new PixelRect(498, 582, 284, 64);
        var dragStart = new Point(area.X, area.Y);
        var dragEnd = new Point(area.Right, area.Bottom);
        Move(Render, rest, dragStart, 20, cross: true);
        overlay.Toolbar.Visibility = Visibility.Hidden;
        Soft(Render(), dragStart, AnimationTick, cross: true, steps: 3);
        const int dragSteps = 24;
        for (var i = 1; i <= dragSteps; i++)
        {
            var t = Smooth((double)i / dragSteps);
            var width = (int)Math.Round(area.Width * t);
            var height = (int)Math.Round(area.Height * t);
            overlay.Surface.DragRect = new PixelRect(area.X, area.Y, width, height);
            Frame(Render(), new Point(area.X + width, area.Y + height), AnimationTick, cross: true);
        }
        overlay.Surface.DragRect = null;
        overlay.Toolbar.Visibility = Visibility.Visible;
        shot.History.Apply(shot.State.AddMark(area));
        Invoke("OpenEditor", 0, true);
        Type(english ? "Make the button brighter and bigger" : "Кнопку ярче и крупнее", dragEnd);
        Soft(shown!, null, 300, key: "Enter", steps: 4);
        Invoke("CommitEditor");
        Soft(Render(), dragEnd, 350, cross: true);

        // Second mark: a click on the typo.
        var typo = new Point(english ? 682 : 742, 443);
        Move(Render, dragEnd, typo, 18, cross: true);
        shot.History.Apply(shot.State.AddMark(new PixelRect((int)typo.X, (int)typo.Y, 0, 0)));
        Invoke("OpenEditor", 1, true);
        Type(english ? "Typo: “projects”" : "Опечатка", typo);
        Soft(shown!, null, 300, key: "Enter", steps: 4);
        Invoke("CommitEditor");
        Soft(Render(), typo, 60, cross: true);

        // The pointer steps aside so the new mark can be read.
        var aside = new Point(typo.X - 60, typo.Y + 70);
        Move(Render, typo, aside, 12, cross: true);
        Frame(Render(), aside, 450, cross: true);
        Soft(Render(), aside, 400, cross: true, key: "Enter");

        // The shot waits in the strip until it's pasted.
        var queue = new ShotQueue();
        queue.Add(shot);
        var stripWindow = new StripWindow();
        stripWindow.Refresh(queue);
        stripWindow.SetStatus(L.Shots(1) + " · " + L.Comments(2), true);
        var strip = RenderWindowContent(stripWindow);
        Soft(page, aside, 800, strip: strip, steps: 8);

        var inField = new Point(420, 618);
        var afterPaste = new Point(1000, 618);
        var final = ShotRenderer.RenderFinal(shot, 0);
        var comments = MessageFormatter.Format([shot.State]);
        var empty = RenderAnimationChat(english, final, comments, 0, 0);
        Soft(empty, aside, 150, strip: strip, steps: 10);
        Move(() => empty, aside, inField, 20, strip: strip);
        Soft(empty, inField, 500, key: "Ctrl+V", strip: strip);
        Soft(empty, inField, AnimationTick, strip: strip, steps: 3);

        // Images land first and the field grows around them, then the comments follow.
        const int pasteSteps = 16;
        for (var i = 1; i <= pasteSteps; i++)
        {
            var t = Smooth((double)i / pasteSteps);
            Frame(RenderAnimationChat(english, final, comments, t, 0), Between(inField, afterPaste, t), AnimationTick, strip: strip);
        }
        Frame(RenderAnimationChat(english, final, comments, 1, 0), afterPaste, 250, strip: strip);
        for (var i = 1; i <= pasteSteps; i++)
            Frame(RenderAnimationChat(english, final, comments, 1, Smooth((double)i / pasteSteps)), afterPaste, AnimationTick, strip: strip);
        Frame(RenderAnimationChat(english, final, comments, 1, 1), afterPaste, 3500, strip: strip);

        // Back to the opening frame, so the loop has no seam.
        FadeTo(opening!, 12);
    }

    private const int AnimationTick = 30;

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static Point Between(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static BitmapSource BlendFrames(BitmapSource from, BitmapSource to, double amount)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(from, new Rect(0, 0, 1280, 720));
            dc.PushOpacity(amount);
            dc.DrawImage(to, new Rect(0, 0, 1280, 720));
            dc.Pop();
        }
        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    /// <summary>The banner comes alive the way the app works: the word gets circled and marked, and the chick is pleased.</summary>
    private static IEnumerable<(BitmapSource Image, int Milliseconds)> AnimationIntro(bool english)
    {
        var art = LoadIntroArt(english);
        var pose = new IntroPose();
        var corner = new Point(IntroFrame.X, IntroFrame.Y);
        var farCorner = new Point(IntroFrame.Right, IntroFrame.Bottom);

        yield return (RenderIntro(art, pose), 350);

        foreach (var shut in new[] { 0.5, 1, 0.5 })
            yield return (RenderIntro(art, pose with { Blink = shut }), 50);
        yield return (RenderIntro(art, pose), 150);

        const int approach = 16;
        for (var i = 1; i <= approach; i++)
        {
            var t = Smooth((double)i / approach);
            yield return (RenderIntro(art, pose with { Cross = Between(new Point(830, 60), corner, t), CrossOpacity = t }), AnimationTick);
        }

        const int drag = 22;
        for (var i = 1; i <= drag; i++)
        {
            var t = Smooth((double)i / drag);
            yield return (RenderIntro(art, pose with { Outline = t, Cross = Between(corner, farCorner, t), CrossOpacity = 1 }), AnimationTick);
        }

        pose = pose with { Outline = 1 };
        const int hop = 26;
        for (var i = 1; i <= hop; i++)
        {
            var t = (double)i / hop;
            yield return (RenderIntro(art, pose with
            {
                Badge = EaseOutBack(Math.Min(1, t * 2)),
                Lift = 20 * Math.Sin(Math.PI * t),
                Tilt = -5 * Math.Sin(2 * Math.PI * t),
                Pulse = 0.05 * Math.Sin(Math.PI * t),
                Cross = Between(farCorner, new Point(farCorner.X + 50, farCorner.Y + 30), Smooth(t)),
                CrossOpacity = 1 - Smooth(Math.Min(1, t * 2)),
            }), AnimationTick);
        }

        pose = pose with { Badge = 1 };
        const int fade = 12;
        for (var i = 1; i <= fade; i++)
            yield return (RenderIntro(art, pose with { Tagline = Smooth((double)i / fade) }), AnimationTick);

        pose = pose with { Tagline = 1 };
        yield return (RenderIntro(art, pose), 350);
        foreach (var shut in new[] { 0.5, 1, 0.5 })
            yield return (RenderIntro(art, pose with { Blink = shut }), 50);
        yield return (RenderIntro(art, pose), 500);
    }

    private static double EaseOutBack(double t)
    {
        const double overshoot = 1.70158;
        var rest = t - 1;
        return 1 + (overshoot + 1) * rest * rest * rest + overshoot * rest * rest;
    }

    /// <summary>All positions are in the banner's own pixels, so they follow make-art.ps1's layout.</summary>
    private readonly record struct IntroPose(double Outline, double Badge, double Lift, double Tilt, double Pulse, double Blink, double Tagline, Point? Cross, double CrossOpacity);

    private sealed record IntroArt(BitmapSource Brackets, BitmapSource Duck, BitmapSource Screen, BitmapSource Chick, BitmapSource Badge, Color Night, FormattedText Tagline);

    private static readonly Int32Rect IntroDuck = new(146, 86, 224, 224);
    private static readonly Int32Rect IntroBrackets = new(110, 50, 300, 300);
    private static readonly Int32Rect IntroScreen = new(470, 140, 470, 140);
    private static readonly Int32Rect IntroChick = new(957, 146, 403, 134);
    private static readonly Int32Rect IntroBadge = new(944, 84, 101, 54);
    private static readonly Rect IntroFrame = new(951, 140, 414, 145);

    private static IntroArt LoadIntroArt(bool english)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(folder.FullName, "docs", "banner.png"))) folder = folder.Parent!;

        var banner = new BitmapImage();
        banner.BeginInit();
        banner.CacheOption = BitmapCacheOption.OnLoad;
        banner.UriSource = new Uri(Path.Combine(folder.FullName, "docs", "banner.png"));
        banner.EndInit();
        banner.Freeze();

        var night = PixelAt(banner, banner.PixelWidth / 2, 20);
        var tagline = PageText(english
            ? "Circle it, comment it, press Ctrl+V. The chat knows exactly what you mean."
            : "Обвёл, подписал, нажал Ctrl+V. Чат сразу понял, о чём ты.", 26, Brushes.White);

        return new IntroArt(
            BannerPart(banner, IntroBrackets, night, cut: IntroDuck),
            BannerPart(banner, IntroDuck, night),
            BannerPart(banner, IntroScreen, night),
            BannerPart(banner, IntroChick, night),
            BannerPart(banner, IntroBadge, night),
            night, tagline);
    }

    /// <summary>A piece of the banner with its background cleared, so pieces can move over each other.</summary>
    private static BitmapSource BannerPart(BitmapSource banner, Int32Rect region, Color night, Int32Rect? cut = null)
    {
        var stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];
        new FormatConvertedBitmap(banner, PixelFormats.Bgra32, null, 0).CopyPixels(region, pixels, stride, 0);

        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var i = y * stride + x * 4;
                var cleared = cut is { } c
                    && region.X + x >= c.X && region.X + x < c.X + c.Width
                    && region.Y + y >= c.Y && region.Y + y < c.Y + c.Height;
                if (cleared || (pixels[i] == night.B && pixels[i + 1] == night.G && pixels[i + 2] == night.R)) pixels[i + 3] = 0;
            }
        }

        var part = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        part.Freeze();
        return part;
    }

    private static BitmapSource RenderIntro(IntroArt art, IntroPose pose)
    {
        const double scale = 0.7, offsetX = 80, offsetY = 150, eyeRadius = 14;
        var beak = FrozenBrush(0xF4, 0x51, 0x1E);
        var eye = new Point(294, 150);

        static Rect Place(Int32Rect region) => new(region.X, region.Y, region.Width, region.Height);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(FrozenBrush(art.Night.R, art.Night.G, art.Night.B), null, new Rect(0, 0, 1280, 720));

            dc.PushTransform(new TranslateTransform(offsetX, offsetY));
            dc.PushTransform(new ScaleTransform(scale, scale));

            dc.PushTransform(new ScaleTransform(1 + pose.Pulse, 1 + pose.Pulse, 260, 200));
            dc.DrawImage(art.Brackets, Place(IntroBrackets));
            dc.Pop();

            dc.PushTransform(new TranslateTransform(0, -pose.Lift));
            dc.PushTransform(new RotateTransform(pose.Tilt, 257, 305));
            dc.DrawImage(art.Duck, Place(IntroDuck));
            if (pose.Blink > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(eye.X - eyeRadius, eye.Y - eyeRadius, 2 * eyeRadius, 2 * eyeRadius * pose.Blink)));
                dc.DrawEllipse(FrozenBrush(0xFF, 0xC4, 0x00), null, eye, eyeRadius, eyeRadius);
                dc.Pop();
                var lid = eye.Y - eyeRadius + 2 * eyeRadius * pose.Blink;
                dc.DrawLine(FrozenPen(PageInk, 3), new Point(eye.X - eyeRadius + 2, lid), new Point(eye.X + eyeRadius - 2, lid));
            }
            dc.Pop();
            dc.Pop();

            dc.DrawImage(art.Screen, Place(IntroScreen));
            dc.DrawImage(art.Chick, Place(IntroChick));

            if (pose.Outline > 0)
            {
                var drawn = new Rect(IntroFrame.X, IntroFrame.Y, IntroFrame.Width * pose.Outline, IntroFrame.Height * pose.Outline);
                var radius = Math.Min(12, Math.Min(drawn.Width, drawn.Height) / 2);
                dc.DrawRoundedRectangle(null, FrozenPen(beak, 5.5), drawn, radius, radius);
            }

            if (pose.Badge > 0)
            {
                dc.PushTransform(new ScaleTransform(pose.Badge, pose.Badge, IntroBadge.X, IntroBadge.Y + IntroBadge.Height));
                dc.DrawImage(art.Badge, Place(IntroBadge));
                dc.Pop();
            }

            dc.Pop();
            dc.Pop();

            if (pose.Tagline > 0)
            {
                dc.PushOpacity(pose.Tagline);
                DrawCentered(dc, art.Tagline, 640, 480);
                dc.Pop();
            }

            if (pose.Cross is { } cross)
            {
                dc.PushOpacity(pose.CrossOpacity);
                DrawCrosshair(dc, new Point(offsetX + cross.X * scale, offsetY + cross.Y * scale));
                dc.Pop();
            }
        }

        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static BitmapSource ComposeAnimationFrame(BitmapSource scene, Point? pointer, bool cross, string? key, BitmapSource? strip, Rect? caret)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(scene, new Rect(0, 0, 1280, 720));
            if (strip != null)
                dc.DrawImage(strip, new Rect(1280 - strip.PixelWidth - 8, (720 - strip.PixelHeight) / 2, strip.PixelWidth, strip.PixelHeight));
            if (caret is { } line) dc.DrawRectangle(PageInk, null, line);

            if (key != null)
            {
                // The strip of page below the cards, so a hint never covers what it's talking about.
                var label = PageText(key, 22, FrozenBrush(0xFF, 0xC4, 0x00), FontWeights.Bold);
                var pill = new Rect(640 - label.Width / 2 - 20, 670, label.Width + 40, label.Height + 12);
                dc.DrawRoundedRectangle(FrozenBrush(0x20, 0x21, 0x24), null, pill, 10, 10);
                dc.DrawText(label, new Point(pill.X + 20, pill.Y + 6));
            }

            if (pointer is { } at)
            {
                if (cross) DrawCrosshair(dc, at);
                else DrawArrow(dc, at);
            }
        }
        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void DrawCrosshair(DrawingContext dc, Point p)
    {
        const double reach = 11;
        foreach (var (pen, extra) in new[] { (FrozenPen(Brushes.White, 3.5), 1.0), (FrozenPen(PageInk, 1.5), 0.0) })
        {
            dc.DrawLine(pen, new Point(p.X - reach - extra, p.Y), new Point(p.X + reach + extra, p.Y));
            dc.DrawLine(pen, new Point(p.X, p.Y - reach - extra), new Point(p.X, p.Y + reach + extra));
        }
    }

    private static void DrawArrow(DrawingContext dc, Point p)
    {
        Point At(double x, double y) => new(p.X + x * 1.25, p.Y + y * 1.25);
        var arrow = new StreamGeometry();
        using (var path = arrow.Open())
        {
            path.BeginFigure(At(0, 0), isFilled: true, isClosed: true);
            path.PolyLineTo([At(0, 18), At(4.5, 14), At(7.5, 21), At(10.5, 20), At(7.5, 13), At(13, 13)], isStroked: true, isSmoothJoin: false);
        }
        dc.DrawGeometry(Brushes.White, FrozenPen(PageInk, 1.25), arrow);
    }

    /// <summary>A made-up chat whose field grows as the pasted image and comments arrive. Those two are whatever the app really pasted.</summary>
    private static BitmapSource RenderAnimationChat(bool english, BitmapSource image, string comments, double imageIn, double textIn)
    {
        const double left = 56, width = 900, bottom = 650, padding = 20, imageHeight = 280, emptyHeight = 64;
        string T(string russian, string other) => english ? other : russian;

        var message = PageText(comments, 16, PageInk);
        message.MaxTextWidth = width - 2 * padding - 56;
        message.LineHeight = 24;

        var withImage = padding + imageHeight + padding;
        var height = emptyHeight + (withImage - emptyHeight) * imageIn + (message.Height + padding) * textIn;
        var top = bottom - height;
        var field = new Rect(left, top, width, height);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(FrozenBrush(0xF6, 0xF7, 0xF9), null, new Rect(0, 0, 1280, 720));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1280, 56));
            dc.DrawLine(PageLine, new Point(0, 56), new Point(1280, 56));
            dc.DrawText(PageText(T("Чат", "Chat"), 20, PageInk, FontWeights.SemiBold), new Point(left, 14));

            var question = PageText(T("Что поправить на странице тарифов?", "What should change on the pricing page?"), 16, PageInk);
            dc.DrawRoundedRectangle(Brushes.White, PageLine, new Rect(left, 136, question.Width + 36, question.Height + 24), 14, 14);
            dc.DrawText(question, new Point(left + 18, 148));

            dc.DrawRoundedRectangle(Brushes.White, PageLine, field, 18, 18);
            dc.PushClip(new RectangleGeometry(field, 18, 18));

            dc.PushOpacity(1 - imageIn);
            dc.DrawText(PageText(T("Напишите сообщение…", "Write a message…"), 16, PageFaint), new Point(left + padding, bottom - emptyHeight + 21));
            dc.Pop();

            var slot = new Rect(left + padding, top + padding, imageHeight * image.PixelWidth / image.PixelHeight, imageHeight);
            dc.PushOpacity(imageIn);
            dc.PushClip(new RectangleGeometry(slot, 10, 10));
            dc.DrawImage(image, slot);
            dc.Pop();
            dc.DrawRoundedRectangle(null, PageLine, slot, 10, 10);
            dc.Pop();

            dc.PushOpacity(textIn);
            dc.DrawText(message, new Point(left + padding, slot.Bottom + padding));
            dc.Pop();
            dc.Pop();

            var send = new Point(left + width - 34, bottom - 32);
            dc.DrawEllipse(FrozenBrush(0xFF, 0xC4, 0x00), null, send, 20, 20);
            DrawCentered(dc, PageText("↑", 20, PageInk, FontWeights.Bold), send.X, send.Y - 14);
        }

        var result = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static readonly Brush PageInk = FrozenBrush(0x20, 0x21, 0x24);
    private static readonly Brush PageMuted = FrozenBrush(0x5F, 0x63, 0x68);
    private static readonly Brush PageFaint = FrozenBrush(0x9A, 0xA0, 0xA6);
    private static readonly Brush PageBlue = FrozenBrush(0x1A, 0x73, 0xE8);
    private static readonly Pen PageLine = FrozenPen(FrozenBrush(0xDA, 0xDC, 0xE0), 1);

    // Each test runs on its own thread, and only frozen objects can cross threads.
    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static FormattedText PageText(string text, double size, Brush brush, FontWeight weight = default)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            weight == default ? FontWeights.Normal : weight, FontStretches.Normal);
#pragma warning disable CS0618 // Offscreen rendering, so there is no window to take a DPI from.
        return new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
#pragma warning restore CS0618
    }

    private static void DrawCentered(DrawingContext dc, FormattedText text, double centerX, double y) =>
        dc.DrawText(text, new Point(centerX - text.Width / 2, y));

    /// <summary>A made-up pricing page with real things to point at: a dull button, a small price and a typo.</summary>
    private static BitmapSource SamplePage(bool english)
    {
        string T(string russian, string other) => english ? other : russian;
        var perMonth = T("в месяц", "per month");
        var action = T("Подключить", "Get started");

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9)), null, new Rect(0, 0, 1280, 720));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1280, 64));
            dc.DrawLine(PageLine, new Point(0, 64), new Point(1280, 64));
            dc.DrawText(PageText(T("Облачко", "Cloudlet"), 22, PageBlue, FontWeights.Bold), new Point(48, 16));
            dc.DrawText(PageText("anna.petrova@example.com", 14, PageMuted), new Point(1060, 22));

            DrawCentered(dc, PageText(T("Выберите тариф", "Choose your plan"), 34, PageInk, FontWeights.SemiBold), 640, 92);
            DrawCentered(dc, PageText(T("Оплата помесячно. Отменить можно в любой момент.", "Billed monthly. Cancel anytime."), 16, PageMuted), 640, 142);

            DrawCard(dc, 110, T("Старт", "Starter"), T("0 ₽", "$0"), 40, PageInk, perMonth, action,
                [T("1 проект", "1 project"), T("2 ГБ в облаке", "2 GB of storage"), T("Поддержка по почте", "Email support")], PageBlue, Brushes.White, false);
            DrawCard(dc, 480, T("Про", "Pro"), T("490 ₽", "$9"), 40, PageInk, perMonth, action,
                [T("Вся история файлов", "Full file history"), T("Безлимитные проэкты", "Unlimited projcts"), T("50 ГБ в облаке", "50 GB of storage")],
                FrozenBrush(0xE8, 0xEA, 0xED), PageFaint, true);
            DrawCard(dc, 850, T("Команда", "Team"), T("1 490 ₽", "$29"), 18, PageFaint, perMonth, action,
                [T("До 20 участников", "Up to 20 members"), T("Приоритетная поддержка", "Priority support"), T("1 ТБ в облаке", "1 TB of storage")], PageBlue, Brushes.White, false);
        }
        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void DrawCard(DrawingContext dc, double x, string name, string price, double priceSize, Brush priceBrush,
        string period, string action, string[] features, Brush button, Brush buttonText, bool highlighted)
    {
        const double width = 320, top = 196, height = 470;
        var edge = highlighted ? new Pen(PageBlue, 2) : PageLine;
        dc.DrawRoundedRectangle(Brushes.White, edge, new Rect(x, top, width, height), 14, 14);
        dc.DrawText(PageText(name, 20, PageInk, FontWeights.SemiBold), new Point(x + 28, top + 24));
        dc.DrawText(PageText(price, priceSize, priceBrush, FontWeights.Bold), new Point(x + 28, top + 84));
        dc.DrawText(PageText(period, 14, PageMuted), new Point(x + 28, top + 142));

        for (var i = 0; i < features.Length; i++)
        {
            var y = top + 192 + i * 44;
            dc.DrawText(PageText("✓", 16, PageBlue, FontWeights.Bold), new Point(x + 28, y));
            dc.DrawText(PageText(features[i], 16, PageInk), new Point(x + 54, y));
        }

        var buttonRect = new Rect(x + 28, top + height - 76, width - 56, 48);
        dc.DrawRoundedRectangle(button, null, buttonRect, 8, 8);
        DrawCentered(dc, PageText(action, 16, buttonText, FontWeights.SemiBold), x + width / 2, buttonRect.Y + 12);
    }

    private static Shot SampleShot(bool english)
    {
        var shot = new Shot(SamplePage(english), 1.0);
        shot.History.Apply(shot.State.AddMark(new PixelRect(498, 582, 284, 64))
            .WithComment(0, english ? "Make the button brighter and bigger" : "Кнопку ярче и крупнее"));
        shot.History.Apply(shot.State.AddMark(new PixelRect(870, 272, 110, 40))
            .WithComment(1, english ? "Make the price stand out" : "Цену сделать заметнее"));

        // The pin sits just past the typo, so its badge doesn't hide it.
        shot.History.Apply(shot.State.AddMark(new PixelRect(english ? 682 : 742, 443, 0, 0))
            .WithComment(2, english ? "Typo: “projects”" : "Опечатка"));
        shot.History.Apply(shot.State.AddRedaction(new RedactionStroke(
            [new PixelPoint(1066, 33), new PixelPoint(1232, 33)], 24)));
        return shot;
    }

    private static Shot SecondSampleShot(bool english)
    {
        var shot = new Shot(SamplePage(english), 1.0);
        shot.History.Apply(shot.State.AddMark(new PixelRect(872, 430, 256, 30))
            .WithComment(0, english ? "Remove this item" : "Этот пункт убрать"));
        shot.History.Apply(shot.State.ApplyCrop(new PixelRect(838, 186, 344, 490)));
        return shot;
    }

    /// <summary>A chat's input field after pasting: the real final images and the real comment text.</summary>
    private static BitmapSource RenderChatField(IReadOnlyList<Shot> shots)
    {
        const double width = 860, padding = 24, imageHeight = 300, gap = 12, scale = 2;

        var text = PageText(MessageFormatter.Format(shots.Select(s => s.State).ToList()), 16, PageInk);
        text.MaxTextWidth = width - 2 * padding;
        text.LineHeight = 24;
        var height = padding + imageHeight + 18 + text.Height + padding;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRoundedRectangle(Brushes.White, PageLine, new Rect(0.5, 0.5, width - 1, height - 1), 20, 20);

            var x = padding;
            for (var i = 0; i < shots.Count; i++)
            {
                var final = ShotRenderer.RenderFinal(shots[i], i);
                var imageWidth = imageHeight * final.PixelWidth / final.PixelHeight;
                var slot = new Rect(x, padding, imageWidth, imageHeight);
                dc.PushClip(new RectangleGeometry(slot, 10, 10));
                dc.DrawImage(final, slot);
                dc.Pop();
                dc.DrawRoundedRectangle(null, PageLine, slot, 10, 10);
                x += imageWidth + gap;
            }

            dc.DrawText(text, new Point(padding, padding + imageHeight + 18));

            var send = new Point(width - padding - 20, height - padding - 20);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), null, send, 20, 20);
            DrawCentered(dc, PageText("↑", 20, PageInk, FontWeights.Bold), send.X, send.Y - 14);
            dc.Pop();
        }

        var image = new RenderTargetBitmap((int)(width * scale), (int)Math.Ceiling(height * scale), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static BitmapSource RenderElement(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource image, string path) =>
        File.WriteAllBytes(path, ClipboardPayload.EncodePng(image));
}
