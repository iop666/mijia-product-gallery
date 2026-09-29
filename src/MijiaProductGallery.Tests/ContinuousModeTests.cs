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
/// 连续滚动 / 瀑布模式语义：不显示页码栏；"每页显示数量"不参与连续模式查询；
/// 切回分页后使用此前保存的每页数量；搜索 / 筛选后状态正确。
/// </summary>
public sealed class ContinuousModeTests : IAsyncLifetime
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
                Model = $"cm.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
                RandomKey = i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);
        await settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, 9);
        await settings.SetValueAsync(AppSettingsKeys.GalleryBrowseMode, AppSettingsKeys.GalleryBrowseModeContinuous);
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
            $"等待超时 [Cards={v.Cards.Count} State={v.State} Total={v.TotalCount} Pager={v.IsPagerVisible}]");
    }

    [Fact]
    public async Task Continuous_HidesPager_AndLoadsAllRows()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 25);
        Assert.False(vm.IsPagerVisible);
        Assert.Equal(25, vm.TotalCount);
    }

    [Fact]
    public async Task Continuous_PageSizeSetting_DoesNotAffectRows()
    {
        // 每页数量即使被改动，连续模式仍加载全部数据（查询逻辑不参与）。
        await settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, 140);
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 25);
        Assert.False(vm.IsPagerVisible);
    }

    [Fact]
    public async Task SwitchBackToPaged_RestoresSavedPageSize()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 25);

        await settings.SetValueAsync(AppSettingsKeys.GalleryBrowseMode, "Paged");
        vm.OnBrowseSettingsChanged();
        await WaitForAsync(() => vm.Cards.Count == 9 && vm.IsPagerVisible);
        Assert.Equal(3, vm.TotalPages);
    }

    [Fact]
    public async Task Search_InContinuousMode_KeepsFullResults_NoPager()
    {
        await vm!.LoadAsync();
        await WaitForAsync(() => vm.Cards.Count == 25);

        vm.ApplySearchImmediate("产品 00");
        await WaitForAsync(() => vm.Cards.Count == 10 && vm.TotalCount == 10);
        Assert.False(vm.IsPagerVisible);
    }
}
