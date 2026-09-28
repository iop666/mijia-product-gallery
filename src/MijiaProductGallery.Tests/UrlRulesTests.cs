using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>URL 规则测试：使用真实接口样本验证 HTML 实体解码与域白名单。</summary>
public sealed class UrlRulesTests
{
    [Fact]
    public void NormalizeImageUrl_DecodesHtmlEntities_FromRealSample()
    {
        var rawIcon = ApiFixture.LoadPtId16Sample()
            .Single(product => product.Model == "lumi.gateway.acn01")
            .RealIcon;

        Assert.Contains("&amp;", rawIcon, StringComparison.Ordinal);

        var normalized = UrlRules.NormalizeImageUrl(rawIcon);

        Assert.DoesNotContain("&amp;", normalized, StringComparison.Ordinal);
        Assert.Contains("&Expires=", normalized, StringComparison.Ordinal);
        Assert.Contains("&Signature=", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeImageUrl_IsIdempotent()
    {
        var rawIcon = ApiFixture.LoadPtId16Sample()
            .Single(product => product.Model == "lumi.gateway.acn01")
            .RealIcon;

        var first = UrlRules.NormalizeImageUrl(rawIcon);
        var second = UrlRules.NormalizeImageUrl(first);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryNormalizeImageUrl_EncodesNonAsciiPath_KeepsQuery()
    {
        const string raw = "https://cdn.cnbj1.fds.api.mi-img.com/米家/产品图.png?Expires=1&Signature=a+b/";

        var ok = UrlRules.TryNormalizeImageUrl(raw, out var normalized);

        Assert.True(ok);
        Assert.StartsWith("https://cdn.cnbj1.fds.api.mi-img.com/%E7%B1%B3%E5%AE%B6/", normalized);
        Assert.EndsWith("?Expires=1&Signature=a+b/", normalized);
    }

    [Theory]
    [InlineData("https://cdn.cnbj1.fds.api.mi-img.com/x.png?a=1", true)]
    [InlineData("https://home.mi.com/cgi-op/api/v1/x", true)]
    [InlineData("https://cdn.cnbj1.fds.api.mi-img.com.evil.example/x.png", false)]
    [InlineData("https://evil.example/x.png", false)]
    [InlineData("ftp://home.mi.com/x.png", false)]
    public void IsAllowedHost_RestrictsToOfficialDomains(string url, bool expected)
    {
        Assert.Equal(expected, UrlRules.IsAllowedHost(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void TryNormalizeImageUrl_RejectsInvalidInput(string? raw)
    {
        Assert.False(UrlRules.TryNormalizeImageUrl(raw, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void NormalizeImageUrl_ThrowsOnInvalidInput()
    {
        Assert.Throws<FormatException>(() => UrlRules.NormalizeImageUrl("not a url"));
    }
}
