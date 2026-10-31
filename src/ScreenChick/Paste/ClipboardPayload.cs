using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenChick.Paste;

internal static class ClipboardPayload
{
    public static DataObject ForImage(byte[] png, byte[] dib)
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png), false);
        data.SetData(DataFormats.Dib, new MemoryStream(dib), false);
        KeepOutOfHistory(data);
        return data;
    }

    public static DataObject ForText(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        KeepOutOfHistory(data);
        return data;
    }

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>CF_DIB: a 24-bit, bottom-up bitmap. Without an alpha channel no app can show it as transparent.</summary>
    public static byte[] EncodeDib(BitmapSource image)
    {
        var bgr = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var width = bgr.PixelWidth;
        var height = bgr.PixelHeight;
        var sourceStride = width * 3;
        var stride = (sourceStride + 3) & ~3;

        var pixels = new byte[sourceStride * height];
        bgr.CopyPixels(pixels, sourceStride, 0);

        const int headerSize = 40;
        var dib = new byte[headerSize + stride * height];
        using (var writer = new BinaryWriter(new MemoryStream(dib)))
        {
            writer.Write(headerSize);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write((short)24);
            writer.Write(0);
            writer.Write(stride * height);
        }

        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * sourceStride, dib, headerSize + (height - 1 - y) * stride, sourceStride);
        }
        return dib;
    }

    /// <summary>Shots can hold private data, so they stay out of Win+V history and cloud clipboard sync.</summary>
    private static void KeepOutOfHistory(DataObject data)
    {
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]), false);
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]), false);
    }
}
