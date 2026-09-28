using SkiaSharp;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>图片测试夹具：运行时生成真实编码的图片字节。</summary>
public static class ImageFixtures
{
    /// <summary>最小 1×1 GIF89a（公开的规范字节序列）。</summary>
    public static byte[] MinimalGif { get; } = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

    public static byte[] CreatePng(int width = 64, int height = 32)
    {
        return Encode(width, height, SKEncodedImageFormat.Png, 100);
    }

    public static byte[] CreateJpeg(int width = 64, int height = 32)
    {
        return Encode(width, height, SKEncodedImageFormat.Jpeg, 90);
    }

    public static byte[] CreateWebp(int width = 64, int height = 32)
    {
        return Encode(width, height, SKEncodedImageFormat.Webp, 80);
    }

    /// <summary>PNG 魔数 + 随机垃圾：头部合法但内容为伪图。</summary>
    public static byte[] FakePng()
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(Enumerable.Range(0, 256).Select(i => (byte)(i * 37)));
        return [.. bytes];
    }

    public static byte[] TruncatedPng()
    {
        var full = CreatePng();
        return full[..(full.Length / 3)];
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, int quality)
    {
        using var bitmap = Draw(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static SKBitmap Draw(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SkyBlue);
        using var paint = new SKPaint { Color = SKColors.OrangeRed };
        canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        return bitmap;
    }
}
