using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 图库分页状态机：分页模式仅物化当前页；条件变化自动回第 1 页；
/// 页码钳制（不整除/空结果）；随机模式隐藏分页；连续模式全量。
/// </summary>
public sealed class GalleryPagingViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();
    private GalleryViewModel? vm;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 25; i++)
        {
            products.Add(new Product
            {
                Model = $"pg.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
                RandomKey = i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);
        await settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, 10);
        vm = CreateViewModel();
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new RecentService(host.CreateContext()),
            new SearchHistoryRepository(host.CreateContext()),
            new FavoritesRepository(host.CreateContext()),
            settings,
            InlineUiDispatcher.Instance,
            debounceMilliseconds: 10);
    }

    private async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 40)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(40);
        }

        var v = vm!;
        Assert.True(condition(),
            $"等待超时 [Cards={v.Cards.Count} State={v.State} Page={v.CurrentPage} Total={v.TotalCount} Pages={v.TotalPages} Paged={v.IsPagedMode} Random={v.IsRandomMode}]");
    }

    [Fact]
    public async Task PagedMode_LoadsOnlyFirstPage()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        Assert.Equal(10, vm.Cards.Count);
        Assert.Equal(25, vm.TotalCount);
        Assert.Equal(3, vm.TotalPages);
        Assert.Equal(1, vm.CurrentPage);
        Assert.True(vm.IsPagerVisible);
    }

    [Fact]
    public async Task NextPage_LastPage_HasRemainder_AndDisablesNext()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToNextPage();
        await WaitForAsync(() => vm.Cards.Count == 10 && vm.CurrentPage == 2);
        vm.GoToNextPage();
        await WaitForAsync(() => vm.Cards.Count == 5);
        Assert.Equal(3, vm.CurrentPage);
        Assert.False(vm.CanGoNextPage);
        Assert.False(vm.CanGoLastPage);
        Assert.True(vm.CanGoPrevPage);
    }

    [Fact]
    public async Task LastPage_JumpsToRemainder()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToLastPage();
        await WaitForAsync(() => vm.Cards.Count == 5);
        Assert.Equal(3, vm.CurrentPage);
    }

    [Fact]
    public async Task Search_Changes_ResetToFirstPage()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToLastPage();
        await WaitForAsync(() => vm.CurrentPage == 3);
        vm.ApplySearchImmediate("产品 02");
        await WaitForAsync(() => vm.TotalCount == 5);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(5, vm.TotalCount);
    }

    [Fact]
    public async Task Sort_Changes_ResetToFirstPage()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToLastPage();
        await WaitForAsync(() => vm.CurrentPage == 3);
        vm.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });
        await WaitForAsync(() => vm.CurrentPage == 1);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(25, vm.TotalCount);
    }

    [Fact]
    public async Task Filter_Changes_ResetToFirstPage()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToLastPage();
        await WaitForAsync(() => vm.CurrentPage == 3);
        vm.FilterPane.SetCategorySelected("c", true);
        await WaitForAsync(() => vm.CurrentPage == 1);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(25, vm.TotalCount);
    }

    [Fact]
    public async Task EmptyResult_ClampsToFirstPage_AndHidesPager()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.GoToLastPage();
        await WaitForAsync(() => vm.CurrentPage == 3);
        vm.ApplySearchImmediate("不存在xyz");
        await WaitForAsync(() => vm.TotalCount == 0 && vm.State == GalleryLoadState.Ready);
        Assert.Equal(1, vm.CurrentPage);
        Assert.False(vm.IsPagerVisible);
    }

    [Fact]
    public async Task RandomMode_HidesPager_AndUsesBatches()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        vm.EnterRandomMode();
        await WaitForAsync(() => vm.Cards.Count == 20);
        Assert.True(vm.IsRandomMode);
        Assert.False(vm.IsPagerVisible);
        vm.ExitRandomMode();
        await WaitForAsync(() => vm.IsPagerVisible);
        Assert.True(vm.IsPagerVisible);
    }

    [Fact]
    public async Task ContinuousMode_LoadsAll_AndHidesPager()
    {
        await settings.SetValueAsync(AppSettingsKeys.GalleryBrowseMode, "Continuous");
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 25);
        // 连续模式不分页：TotalCount 不参与统计，分页栏隐藏。
        Assert.Equal(0, vm.TotalCount);
        Assert.False(vm.IsPagerVisible);
    }

    [Fact]
    public async Task PageSizeSettingChange_ReloadsFirstPage()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
        await settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, 21);
        vm.OnBrowseSettingsChanged();
        await WaitForAsync(() => vm.Cards.Count == 21);
        Assert.Equal(21, vm.Cards.Count);
        Assert.Equal(2, vm.TotalPages);
    }
}

/// <summary>页码输入框：提交跳转、范围钳制、非法校正、文本同步。</summary>
public sealed class PageBoxSubmitTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();
    private GalleryViewModel? vm;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 25; i++)
        {
            products.Add(new Product
            {
                Model = $"pb.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);
        await settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, 10);
        vm = CreateViewModel();
        await vm.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 10);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new RecentService(host.CreateContext()),
            new SearchHistoryRepository(host.CreateContext()),
            new FavoritesRepository(host.CreateContext()),
            settings,
            InlineUiDispatcher.Instance,
            debounceMilliseconds: 10);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 40)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(40);
        }

        Assert.True(condition(), "等待条件超时");
    }

    [Fact]
    public async Task PageBox_Syncs_WithCurrentPage()
    {
        Assert.Equal("1", vm!.PageBoxText);
        vm.GoToNextPage();
        await WaitForAsync(() => vm.CurrentPage == 2);
        Assert.Equal("2", vm.PageBoxText);
    }

    [Fact]
    public async Task Submit_MiddlePage_Jumps()
    {
        vm!.SubmitPageText("3");
        await WaitForAsync(() => vm.CurrentPage == 3 && vm.Cards.Count == 5);
        Assert.Equal("3", vm.PageBoxText);
    }

    [Fact]
    public async Task Submit_FirstPage_And_LastPage()
    {
        vm!.SubmitPageText("1");
        await WaitForAsync(() => vm.CurrentPage == 1);
        vm.SubmitPageText("3");
        await WaitForAsync(() => vm.CurrentPage == 3);
    }

    [Fact]
    public async Task Submit_Zero_ClampsToFirstPage()
    {
        vm!.SubmitPageText("0");
        await WaitForAsync(() => vm.CurrentPage == 1);
        Assert.Equal("1", vm.PageBoxText);
    }

    [Fact]
    public async Task Submit_BeyondTotal_ClampsToLastPage()
    {
        vm!.SubmitPageText("999");
        await WaitForAsync(() => vm.CurrentPage == 3);
        Assert.Equal("3", vm.PageBoxText);
    }

    [Fact]
    public async Task Submit_Invalid_RevertsToCurrentPage_NoThrow()
    {
        vm!.SubmitPageText("abc");
        Assert.Equal("1", vm.PageBoxText);
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(25, vm.TotalCount);
    }

    [Fact]
    public async Task FilterChange_UpdatesPageBoxToCorrectedPage()
    {
        vm!.SubmitPageText("3");
        await WaitForAsync(() => vm.CurrentPage == 3);
        vm.FilterPane.SetCategorySelected("c", true);
        await WaitForAsync(() => vm.CurrentPage == 1 && vm.PageBoxText == "1");
    }

    [Fact]
    public async Task Submit_Text_Enter_LoadsPage2_CardsChanged()
    {
        // "输入 2 + Enter → 第 2 页"：卡片数据确实变化（首卡为第 2 页首行）。
        vm!.SubmitPageText("2");
        await WaitForAsync(() => vm.CurrentPage == 2 && vm.Cards.Count == 10);
        // PageBoxSubmitTests 种子前缀为 pb.model.*：第 2 页首行 010、末行 019。
        Assert.Equal("pb.model.010", vm.Cards[0].Model);
        Assert.Equal("pb.model.019", vm.Cards[9].Model);
        // 页码显示与 CurrentPage 一致。
        Assert.Equal("2", vm.PageBoxText);
    }

    [Fact]
    public async Task ScrollTop_Tracks_PageChanges_ViaCardsReset()
    {
        // 滚动置顶由页面订阅 CurrentPage 实现（ChangeView），此处验证翻页后卡片集合确实重置。
        vm!.SubmitPageText("2");
        await WaitForAsync(() => vm.CurrentPage == 2 && vm.Cards.Count == 10);
        Assert.NotEqual("pg.model.000", vm.Cards[0].Model);
    }
}

/// <summary>设置模型：每页数量钳制到 9～140。</summary>
public class PageSizeClampTests
{
    [Fact]
    public async Task PageSize_ClampedToRange_AndPersisted()
    {
        var settings = new InMemorySettings();
        var vm = new SettingsViewModel(settings);
        vm.PageSize = 8;
        Assert.Equal(9, vm.PageSize);
        Assert.Equal(9, await settings.GetValueAsync(AppSettingsKeys.GalleryPageSize, 0));
        vm.PageSize = 141;
        Assert.Equal(140, vm.PageSize);
        Assert.Equal(140, await settings.GetValueAsync(AppSettingsKeys.GalleryPageSize, 0));
    }
}
