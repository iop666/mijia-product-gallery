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
        await WaitForAsync(() => vm.TotalCount == 0 && vm.State == GalleryLoadState.Empty);
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
