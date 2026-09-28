using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>接口数据标准化测试：真实样本字段清理、型号校验与 ptId 无关性。</summary>
public sealed class BaikeProductNormalizerTests
{
    [Fact]
    public void Normalize_TrimsFields_AndIgnoresProductPtId()
    {
        var dto = new BaikeProductDto
        {
            Model = " xiaomi.repeater.v3 ",
            Name = " 小米Wi-Fi放大器Pro ",
            Brand = " 小米出品 ",
            RealIcon = "https://cdn.cnbj1.fds.api.mi-img.com/x.png",
            CreateTimeUnix = 1_554_102_321,
            UpdateTimeUnix = 1_608_714_662,
            PtId = 31,
        };

        var remote = BaikeProductNormalizer.Normalize(dto, "路由网关");

        Assert.Equal("xiaomi.repeater.v3", remote.Model);
        Assert.Equal("小米Wi-Fi放大器Pro", remote.Name);
        Assert.Equal("小米出品", remote.Brand);
        Assert.Equal("路由网关", remote.Category);
        Assert.Equal(1_554_102_321, remote.CreateTimeUnix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("含有 空格")]
    [InlineData("bad/model")]
    public void Normalize_RejectsInvalidModel(string model)
    {
        var dto = new BaikeProductDto
        {
            Model = model,
            Name = "n",
            Brand = "b",
            RealIcon = "https://cdn.cnbj1.fds.api.mi-img.com/x.png",
        };

        Assert.Throws<FormatException>(() => BaikeProductNormalizer.Normalize(dto, "其他"));
    }

    [Fact]
    public void RealApiSample_CategoryComesFromMembership_NotFromProductPtId()
    {
        var ptIdNames = new Dictionary<int, string> { [16] = "路由网关", [12] = "照明", [31] = "未登记分类" };
        var category = CategoryRules.ResolveCategory([16], ptIdNames);

        var repeater = BaikeProductNormalizer.Normalize(
            ApiFixture.LoadPtId16Sample().Single(product => product.Model == "xiaomi.repeater.v3"),
            category);
        var gateway = BaikeProductNormalizer.Normalize(
            ApiFixture.LoadPtId16Sample().Single(product => product.Model == "lumi.gateway.acn01"),
            category);

        Assert.Equal(31, ApiFixture.LoadPtId16Sample().Single(product => product.Model == "xiaomi.repeater.v3").PtId);
        Assert.Equal(12, ApiFixture.LoadPtId16Sample().Single(product => product.Model == "lumi.gateway.acn01").PtId);
        Assert.Equal("路由网关", repeater.Category);
        Assert.Equal("路由网关", gateway.Category);
    }
}
