using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 图片文件头判定与校验规则：扩展名不可信，一切以字节内容为准。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static class ImageHeaderRules
{
    /// <summary>单张样图的字节上限。</summary>
    public const long MaxImageLength = 20 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>按文件头魔数判定实际格式；无法识别返回 Unknown。</summary>
    public static ImageFormat DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return ImageFormat.Png;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ImageFormat.Jpg;
        }

        if (IsGifSignature(bytes))
        {
            return ImageFormat.Gif;
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return ImageFormat.Webp;
        }

        return ImageFormat.Unknown;
    }

    /// <summary>格式对应的标准扩展名；Unknown 抛出异常（调用方应先判定格式）。</summary>
    public static string GetExtension(ImageFormat format)
    {
        return format switch
        {
            ImageFormat.Png => ".png",
            ImageFormat.Jpg => ".jpg",
            ImageFormat.Gif => ".gif",
            ImageFormat.Webp => ".webp",
            _ => throw new ImageValidationException("无法识别的图片格式，不能确定扩展名"),
        };
    }

    private static bool IsGifSignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6)
        {
            return false;
        }

        return bytes[0] == (byte)'G'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8'
            && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
            && bytes[5] == (byte)'a';
    }
}
