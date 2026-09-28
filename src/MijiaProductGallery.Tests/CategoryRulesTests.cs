using MijiaProductGallery.Core.Rules;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>归类规则测试：货架与真实大类的优先级、ptId 无关性与确定性。</summary>
public sealed class CategoryRulesTests
{
    private static readonly Dictionary<int, string> PtIdNames = new()
    {
        [-10000] = "新上线",
        [8] = "运动健康",
        [12] = "照明",
        [13] = "传感器",
        [16] = "路由网关",
        [23] = "个护与起居",
    };

    [Fact]
    public void RealCategoryOnly_ResolvesByMembership()
    {
        Assert.Equal(
            "路由网关",
            CategoryRules.ResolveCategory([16], PtIdNames));
    }

    [Fact]
    public void ShelfOnly_GoesToNewArrival()
    {
        Assert.Equal(
            "新上线",
            CategoryRules.ResolveCategory([-10000], PtIdNames));
    }

    [Fact]
    public void ShelfAndRealCategory_RealCategoryWins()
    {
        Assert.Equal(
            "个护与起居",
            CategoryRules.ResolveCategory([-10000, 23], PtIdNames));
    }

    [Fact]
    public void MultipleRealCategories_PicksSmallestPtId()
    {
        Assert.Equal(
            "运动健康",
            CategoryRules.ResolveCategory([13, 8], PtIdNames));
    }

    [Fact]
    public void UnknownPtId_ReturnsMarkedUnknown()
    {
        Assert.Equal(
            "未分类(99)",
            CategoryRules.ResolveCategory([99], PtIdNames));
    }

    [Fact]
    public void EmptyMembership_ReturnsUnknown()
    {
        Assert.Equal(
            "未分类",
            CategoryRules.ResolveCategory([], PtIdNames));
    }
}
