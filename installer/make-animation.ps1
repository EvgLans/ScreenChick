# Builds docs\demo.gif and docs\demo-en.gif from frames the app's own code renders offscreen.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$frames = Join-Path $env:TEMP 'screenchick-animation'
$project = Join-Path $root 'tests\ScreenChick.Tests\ScreenChick.Tests.csproj'

$env:SCREENCHICK_ANIMATION_DIR = $frames
& dotnet test $project --filter 'FullyQualifiedName~Writes_animation_frames_when_asked' --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Rendering the frames failed.' }
Remove-Item Env:\SCREENCHICK_ANIMATION_DIR

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public static class GifMaker
{
    private const int Transparent = 255;

    private sealed class Box
    {
        public List<KeyValuePair<int, int>> Colors;
        public int Range;
        public int Channel;
        public double Score;

        public Box(List<KeyValuePair<int, int>> colors)
        {
            Colors = colors;
            for (var channel = 0; channel < 3; channel++)
            {
                var shift = channel * 8;
                var values = colors.Select(c => (c.Key >> shift) & 255).ToList();
                var range = values.Max() - values.Min();
                if (range >= Range) { Range = range; Channel = channel; }
            }
            // Rare colors still get room: the small orange outlines matter as much as the white page.
            Score = Range * Math.Sqrt(colors.Sum(c => Math.Log(c.Value + 1.0)));
        }
    }

    public static void Make(string framesDir, string output)
    {
        var files = Directory.GetFiles(framesDir, "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        int width, height;
        var counts = new Dictionary<int, int>();

        // Fades would crowd out the exact colors of the settled frames, and they pass too quickly to need their own.
        foreach (var file in files.Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_f")))
        {
            foreach (var pixel in Load(file, out width, out height))
            {
                int seen;
                counts.TryGetValue(pixel, out seen);
                counts[pixel] = seen + 1;
            }
        }

        var palette = BuildPalette(counts);
        var nearest = new Dictionary<int, byte>();

        Load(files[0], out width, out height);
        using (var stream = new FileStream(output, FileMode.Create))
        {
            WriteHeader(stream, width, height, palette);
            byte[] canvas = null;
            foreach (var file in files)
            {
                var pixels = Load(file, out width, out height);
                var mapped = new byte[width * height];
                for (var i = 0; i < mapped.Length; i++)
                {
                    byte index;
                    if (!nearest.TryGetValue(pixels[i], out index))
                    {
                        index = Nearest(palette, pixels[i]);
                        nearest[pixels[i]] = index;
                    }
                    mapped[i] = index;
                }
                var delay = int.Parse(Path.GetFileNameWithoutExtension(file).Split('_')[1]) / 10;
                WriteFrame(stream, mapped, canvas, width, height, delay);
                canvas = mapped;
            }
            stream.WriteByte(0x3B);
        }
    }

    private static int[] Load(string file, out int width, out int height)
    {
        using (var image = new Bitmap(file))
        {
            width = image.Width;
            height = image.Height;
            var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new int[width * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            image.UnlockBits(data);
            for (var i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
            return pixels;
        }
    }

    private static int[] BuildPalette(Dictionary<int, int> counts)
    {
        var boxes = new List<Box> { new Box(counts.ToList()) };
        while (boxes.Count < Transparent)
        {
            var box = boxes.Where(b => b.Colors.Count > 1).OrderByDescending(b => b.Score).FirstOrDefault();
            if (box == null) break;
            boxes.Remove(box);
            var shift = box.Channel * 8;
            var sorted = box.Colors.OrderBy(c => (c.Key >> shift) & 255).ToList();
            var half = sorted.Sum(c => Math.Log(c.Value + 1.0)) / 2;
            var cut = 0;
            for (var sum = 0.0; cut < sorted.Count - 1 && sum < half; cut++) sum += Math.Log(sorted[cut].Value + 1.0);
            cut = Math.Max(1, cut);
            boxes.Add(new Box(sorted.Take(cut).ToList()));
            boxes.Add(new Box(sorted.Skip(cut).ToList()));
        }

        var palette = new int[256];
        for (var i = 0; i < boxes.Count; i++)
        {
            double total = 0, r = 0, g = 0, b = 0;
            foreach (var color in boxes[i].Colors)
            {
                total += color.Value;
                r += ((color.Key >> 16) & 255) * (double)color.Value;
                g += ((color.Key >> 8) & 255) * (double)color.Value;
                b += (color.Key & 255) * (double)color.Value;
            }
            palette[i] = ((int)Math.Round(r / total) << 16) | ((int)Math.Round(g / total) << 8) | (int)Math.Round(b / total);
        }
        return palette;
    }

    private static byte Nearest(int[] palette, int color)
    {
        int r = (color >> 16) & 255, g = (color >> 8) & 255, b = color & 255;
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Transparent; i++)
        {
            int dr = r - ((palette[i] >> 16) & 255), dg = g - ((palette[i] >> 8) & 255), db = b - (palette[i] & 255);
            var distance = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return (byte)best;
    }

    private static void WriteHeader(Stream s, int width, int height, int[] palette)
    {
        Bytes(s, System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        Word(s, width);
        Word(s, height);
        Bytes(s, new byte[] { 0xF7, 0, 0 });
        foreach (var color in palette)
            Bytes(s, new[] { (byte)(color >> 16), (byte)(color >> 8), (byte)color });
        Bytes(s, new byte[] { 0x21, 0xFF, 11 });
        Bytes(s, System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        Bytes(s, new byte[] { 3, 1, 0, 0, 0 });
    }

    // Only the changed rectangle is stored, with untouched pixels transparent, which keeps a mostly still UI small.
    private static void WriteFrame(Stream s, byte[] mapped, byte[] previous, int width, int height, int delay)
    {
        int left = 0, top = 0, right = width - 1, bottom = height - 1;
        if (previous != null)
        {
            left = width; top = height; right = 0; bottom = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (mapped[y * width + x] != previous[y * width + x])
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
            if (left > right) { left = 0; top = 0; right = 0; bottom = 0; }
        }

        var w = right - left + 1;
        var h = bottom - top + 1;
        var pixels = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var at = (y + top) * width + x + left;
                pixels[y * w + x] = previous != null && previous[at] == mapped[at] ? (byte)Transparent : mapped[at];
            }

        Bytes(s, new byte[] { 0x21, 0xF9, 4, 0x05 });
        Word(s, delay);
        Bytes(s, new byte[] { Transparent, 0, 0x2C });
        Word(s, left);
        Word(s, top);
        Word(s, w);
        Word(s, h);
        s.WriteByte(0);
        s.WriteByte(8);
        Compress(s, pixels);
    }

    private static void Compress(Stream s, byte[] pixels)
    {
        const int clear = 256, end = 257;
        var block = new byte[255];
        var filled = 0;
        var bits = 0;
        var bitCount = 0;
        var size = 9;

        Action<int> emit = code =>
        {
            bits |= code << bitCount;
            bitCount += size;
            while (bitCount >= 8)
            {
                block[filled++] = (byte)bits;
                bits >>= 8;
                bitCount -= 8;
                if (filled == 255) { s.WriteByte(255); s.Write(block, 0, 255); filled = 0; }
            }
        };

        var table = new Dictionary<int, int>();
        var next = 258;
        emit(clear);
        int prefix = pixels[0];
        for (var i = 1; i < pixels.Length; i++)
        {
            int key = (prefix << 8) | pixels[i], code;
            if (table.TryGetValue(key, out code)) { prefix = code; continue; }
            emit(prefix);
            if (next == 4096)
            {
                emit(clear);
                table.Clear();
                next = 258;
                size = 9;
            }
            else
            {
                table[key] = next++;
                if (next > (1 << size) && size < 12) size++;
            }
            prefix = pixels[i];
        }
        emit(prefix);
        emit(end);
        if (bitCount > 0)
        {
            block[filled++] = (byte)bits;
            if (filled == 255) { s.WriteByte(255); s.Write(block, 0, 255); filled = 0; }
        }
        if (filled > 0) { s.WriteByte((byte)filled); s.Write(block, 0, filled); }
        s.WriteByte(0);
    }

    private static void Word(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
    }

    private static void Bytes(Stream s, byte[] bytes)
    {
        s.Write(bytes, 0, bytes.Length);
    }
}
'@

foreach ($item in @(@('ru', 'demo.gif'), @('en', 'demo-en.gif'))) {
    $output = Join-Path $root "docs\$($item[1])"
    [GifMaker]::Make((Join-Path $frames $item[0]), $output)
    '{0}: {1:N1} MB' -f $item[1], ((Get-Item $output).Length / 1MB)
}

Remove-Item $frames -Recurse -Force
