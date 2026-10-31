using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        var shot = SampleShot();
        Save(ShotRenderer.RenderFinal(shot, 0), Path.Combine(dir, "final.png"));

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

        var overlay = new OverlayWindow(shot, 0, new Capture.PixelBounds(0, 0, 1280, 720));
        Save(RenderElement(overlay.Root, 1280, 720), Path.Combine(dir, "overlay.png"));

        var queue = new ShotQueue();
        queue.Add(shot);
        queue.Add(SampleShot());
        var strip = new StripWindow();
        strip.Refresh(queue);
        strip.SetStatus("2 снимка · 4 комментария", true);
        var content = (FrameworkElement)strip.Content;
        strip.Content = null;
        Save(RenderElement(content, 206, 400), Path.Combine(dir, "strip.png"));
    });

    private static Shot SampleShot()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)), null, new Rect(0, 0, 1280, 720));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED)), null, new Rect(0, 0, 1280, 64));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)), null, new Rect(80, 120, 1120, 220));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), null, new Rect(120, 270, 160, 44));
            for (var i = 0; i < 3; i++)
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), new Rect(80 + i * 380, 380, 360, 280));
        }
        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();

        var shot = new Shot(image, 1.0);
        shot.History.Apply(shot.State.AddMark(new PixelRect(110, 262, 180, 60)).WithComment(0, "Кнопку ярче и крупнее"));
        shot.History.Apply(shot.State.AddMark(new PixelRect(860, 600, 0, 0)).WithComment(1, "Цену больше"));
        shot.History.Apply(shot.State.AddRedaction(new RedactionStroke(
            [new PixelPoint(120, 440), new PixelPoint(380, 440)], 26)));
        return shot;
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
