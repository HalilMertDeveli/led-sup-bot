using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace LedSupBot.UnitTests.Media;

internal static class TestImageBytes
{
    public static byte[] CreatePng(int width = 8, int height = 8)
    {
        using var image = new Image<Rgba32>(width, height);
        image[0, 0] = new Rgba32(255, 0, 0, 255);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateJpeg(int width = 8, int height = 8)
    {
        using var image = new Image<Rgba32>(width, height);
        image[0, 0] = new Rgba32(0, 255, 0, 255);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateWebp(int width = 8, int height = 8)
    {
        using var image = new Image<Rgba32>(width, height);
        image[0, 0] = new Rgba32(0, 0, 255, 255);
        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateGif(int width = 4, int height = 4)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new GifEncoder());
        return ms.ToArray();
    }

    public static byte[] CorruptJpegHeader()
    {
        var jpeg = CreateJpeg();
        jpeg[0] = 0x00;
        return jpeg;
    }
}
