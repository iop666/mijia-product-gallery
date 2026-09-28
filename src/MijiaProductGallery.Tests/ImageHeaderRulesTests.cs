using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>文件头判定规则测试：魔数识别与扩展名映射。</summary>
public sealed class ImageHeaderRulesTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif87a")]
    [InlineData("gif89a")]
    [InlineData("webp")]
    [InlineData("unknown")]
    public void DetectFormat_RecognizesMagicNumbers(string caseName)
    {
        var bytes = caseName switch
        {
            "png" => ImageFixtures.CreatePng(),
            "jpg" => ImageFixtures.CreateJpeg(),
            "gif87a" => "GIF87a\x01\x00\x01\x00\x80\x00\x00\x00\x00\x00\xFF\xFF\xFF\x2C\x00\x00\x00\x00\x01\x00\x01\x00\x00\x02\x02\x44\x01\x00\x3B"u8.ToArray(),
            "gif89a" => ImageFixtures.MinimalGif,
            "webp" => ImageFixtures.CreateWebp(),
            _ => "just some text bytes, definitely not an image"u8.ToArray(),
        };

        var expected = caseName switch
        {
            "png" => ImageFormat.Png,
            "jpg" => ImageFormat.Jpg,
            "gif87a" or "gif89a" => ImageFormat.Gif,
            "webp" => ImageFormat.Webp,
            _ => ImageFormat.Unknown,
        };

        Assert.Equal(expected, ImageHeaderRules.DetectFormat(bytes));
    }

    [Fact]
    public void GetExtension_MapsFormatToStandardExtension()
    {
        Assert.Equal(".png", ImageHeaderRules.GetExtension(ImageFormat.Png));
        Assert.Equal(".jpg", ImageHeaderRules.GetExtension(ImageFormat.Jpg));
        Assert.Equal(".gif", ImageHeaderRules.GetExtension(ImageFormat.Gif));
        Assert.Equal(".webp", ImageHeaderRules.GetExtension(ImageFormat.Webp));
        Assert.Throws<ImageValidationException>(() => ImageHeaderRules.GetExtension(ImageFormat.Unknown));
    }
}
