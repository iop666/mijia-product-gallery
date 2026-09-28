using MijiaProductGallery.Core.Enums;
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

/// <summary>图库排序视图模型测试：应用排序、持久化、启动恢复、清除行为。</summary>
public sealed class GallerySortViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var store = new ImageStore(host.Paths);
        var image = await store.StoreNewAsync("m-a", new MemoryStream(ImageFixtures.CreatePng()));
        await new ProductRepository(host.CreateContext()).AddRangeAsync(
        [
            new Product
            {
                Model = "m-a",
                Name = "name-aa",
                Brand = "b",
                Category = "c",
                ImageFileName = image.ImageFileName,
                ImagePath = image.ImagePath,
                Sha256 = image.Sha256,
                FirstSeenUnix = 400,
            },
            new Product
            {
                Model = "m-b",
                Name = "name-bb",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 100,
            },
        ]);
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
            new SearchHistoryRepository(host.CreateContext()),
            settings,
            InlineUiDispatcher.Instance,
            debounceMilliseconds: 10);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 40)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(40);
        }

        return condition();
    }

    [Fact]
    public async Task ApplySort_UsageDescending_ReordersCards()
    {
        await using var context = host.CreateContext();
        var usageRepository = new UsageRepository(context);
        var productA = (await new ProductRepository(host.CreateContext()).GetByModelAsync("m-a"))!;
        var productB = (await new ProductRepository(host.CreateContext()).GetByModelAsync("m-b"))!;
        for (var i = 0; i < 5; i++)
        {
            await usageRepository.RecordAsync(productB.Id, UsageType.View, 1_700_000_000 + i);
        }

        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.ApplySort(new ProductSort { Field = ProductSortField.UsageCount, Direction = SortDirection.Descending });
        Assert.True(await WaitForAsync(() => vm.Cards.Count == 2 && vm.Cards[0].Model == "m-b"));
        Assert.Equal(productB.Id, vm.Cards[0].ProductId);
    }

    [Fact]
    public async Task ApplySort_PersistsSortState()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });

        var saved = await settings.GetValueAsync(
            "Gallery.SortState",
            new GalleryViewModel.SortStateDto());
        Assert.Equal("Name", saved.Field);
        Assert.Equal("Descending", saved.Direction);
    }

    [Fact]
    public async Task LoadAsync_RestoresSortFromSettings()
    {
        var first = CreateViewModel();
        await first.LoadAsync();
        first.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });

        // 新实例（模拟重启）从设置恢复排序。
        var second = CreateViewModel();
        await second.LoadAsync();

        Assert.NotNull(second.Sort);
        Assert.Equal(ProductSortField.Name, second.Sort.Field);
        Assert.Equal(SortDirection.Descending, second.Sort.Direction);
        Assert.Equal(["m-b", "m-a"], second.Cards.Select(card => card.Model).ToList());
    }

    [Fact]
    public async Task ApplySort_Null_RestoresDefaultModelOrder()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });
        await WaitForAsync(() => vm.Cards.Count == 2 && vm.Cards[0].Model == "m-b");

        vm.ApplySort(null);
        await WaitForAsync(() => vm.Cards.Count == 2 && vm.Cards[0].Model == "m-a");

        Assert.Null(vm.Sort);
    }

    [Fact]
    public async Task ClearAllFilters_KeepsSort()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });
        await WaitForAsync(() => vm.Cards.Count == 2);

        vm.ClearAllFilters();
        await WaitForAsync(() => vm.Cards.Count == 2);

        Assert.NotNull(vm.Sort);
        Assert.Equal(ProductSortField.Name, vm.Sort.Field);
        Assert.Equal(["m-b", "m-a"], vm.Cards.Select(card => card.Model).ToList());
    }

    [Fact]
    public async Task ApplySort_Twice_LastWins()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.ApplySort(new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending });
        vm.ApplySort(new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Ascending });

        Assert.True(await WaitForAsync(() =>
            vm.Sort is { Field: ProductSortField.Model, Direction: SortDirection.Ascending }));
        Assert.Equal(["m-a", "m-b"], vm.Cards.Select(card => card.Model).ToList());
    }
}
