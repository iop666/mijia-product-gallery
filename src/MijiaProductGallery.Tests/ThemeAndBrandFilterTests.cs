using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 窗口级主题覆盖表：实时主题切换不重启应用的实现基础——
/// 三种明确定义的主题返回各自的窗口背景覆盖集，跟随系统返回空集。
/// </summary>
public class ThemePaletteDefinitionTests
{
    [Fact]
    public void Dark_HasWindowBackgroundOverrides()
    {
        var palette = ThemePaletteDefinition.For("Dark");
        Assert.True(palette.Count > 0);
        Assert.True(palette.ContainsKey("ApplicationPageBackgroundThemeBrush"));
        Assert.Equal(0xFF202020u, palette["ApplicationPageBackgroundThemeBrush"]);
    }

    [Fact]
    public void Gray_HasAdobeStyleGrayBackground()
    {
        var palette = ThemePaletteDefinition.For("Gray");
        Assert.True(palette.Count > 0);
        Assert.Equal(0xFF3B3B3Bu, palette["ApplicationPageBackgroundThemeBrush"]);
    }

    [Fact]
    public void Light_HasExplicitWhiteWindowBackground()
    {
        var palette = ThemePaletteDefinition.For("Light");
        Assert.True(palette.Count > 0);
        Assert.Equal(0xFFFFFFFFu, palette["ApplicationPageBackgroundThemeBrush"]);
    }

    [Fact]
    public void System_AndNull_AreEmpty_FollowSystem()
    {
        Assert.Empty(ThemePaletteDefinition.For("System"));
        Assert.Empty(ThemePaletteDefinition.For(null));
    }

    [Fact]
    public void AllDefinedThemes_OverrideTheSameKeySet()
    {
        foreach (var theme in new[] { "Dark", "Gray", "Light" })
        {
            var palette = ThemePaletteDefinition.For(theme);
            Assert.Equal(ThemePaletteDefinition.Keys.Count, palette.Count);
            foreach (var key in ThemePaletteDefinition.Keys)
            {
                Assert.True(palette.ContainsKey(key), $"{theme} 缺少 {key}");
            }
        }
    }
}

/// <summary>品牌搜索过滤：FilterPaneViewModel.FilteredBrands 随搜索词即时收窄/恢复。</summary>
public class BrandSearchFilterTests
{
    private static FilterPaneViewModel CreatePane()
    {
        var pane = new FilterPaneViewModel();
        var brands = new[] { "小米出品", "MIJIA米家", "70迈", "8H", "Antjiujiu" };
        foreach (var label in brands)
        {
            pane.Brands.Add(new FilterOption(label));
        }

        pane.ApplyBrandFilter();
        return pane;
    }

    [Fact]
    public void EmptySearch_ShowsAllBrands()
    {
        var pane = CreatePane();
        Assert.Equal(5, pane.FilteredBrands.Count);
    }

    [Fact]
    public void Search_NarrowsToMatchingBrands()
    {
        var pane = CreatePane();
        pane.BrandSearchText = "米";
        Assert.Equal(2, pane.FilteredBrands.Count);
        Assert.All(pane.FilteredBrands, b => Assert.Contains("米", b.Label));
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var pane = CreatePane();
        pane.BrandSearchText = "ant";
        Assert.Single(pane.FilteredBrands);
        Assert.Equal("Antjiujiu", pane.FilteredBrands[0].Label);
    }

    [Fact]
    public void ClearingSearch_RestoresFullList()
    {
        var pane = CreatePane();
        pane.BrandSearchText = "8H";
        Assert.Single(pane.FilteredBrands);
        pane.BrandSearchText = string.Empty;
        Assert.Equal(5, pane.FilteredBrands.Count);
    }

    [Fact]
    public void Selection_SurvivesFiltering()
    {
        var pane = CreatePane();
        pane.SetBrandSelected("8H", true);
        pane.BrandSearchText = "8H";
        Assert.True(pane.FilteredBrands.Single(b => b.Label == "8H").IsSelected);
        pane.BrandSearchText = string.Empty;
        Assert.True(pane.Brands.Single(b => b.Label == "8H").IsSelected);
    }
}
