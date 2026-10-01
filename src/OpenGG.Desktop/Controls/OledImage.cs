using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OneRGB.Application.Devices.Keyboard;

namespace OpenGG.Desktop.Controls;

public static class OledImage
{
    public static OledFrame Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Choose an image up to 16 MB.");
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 32_000_000 || frame.PixelWidth > 32768 || frame.PixelHeight > 32768)
            throw new InvalidDataException("Choose an image up to 32 megapixels.");
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = Math.Min(512, frame.PixelWidth);
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return FromBitmap(image);
    }

    public static OledFrame FromBitmap(BitmapSource image)
    {
        // Fit without stretching; transparent pixels and letterboxing become OLED black.
        var scale = Math.Min(128.0 / image.PixelWidth, 40.0 / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 128, 40));
            dc.DrawImage(image, new Rect((128 - width) / 2, (40 - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(128, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[128 * 40 * 4];
        var gray = new byte[128 * 40];
        bitmap.CopyPixels(pixels, 128 * 4, 0);
        for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)((77 * pixels[i * 4 + 2] + 150 * pixels[i * 4 + 1] + 29 * pixels[i * 4]) >> 8);
        return OledFrame.FromGrayscale(128, 40, gray);
    }

    public static BitmapSource Render(OledFrame frame)
    {
        var pixels = new byte[frame.Width * frame.Height];
        for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
                pixels[y * frame.Width + x] = frame[x, y] ? (byte)255 : (byte)0;
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, pixels, frame.Width);
        bitmap.Freeze();
        return bitmap;
    }
}
