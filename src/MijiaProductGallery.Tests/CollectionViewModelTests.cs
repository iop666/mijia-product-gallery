using Microsoft.EntityFrameworkCore;
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
/// 收藏夹切换：进入收藏视图载入合集列表（默认项固定首位）；
/// 切换合集仅展示合集成员（与星标收藏独立）；删除所选合集后回落默认并重查。
/// </summary>
public sealed class CollectionViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();
    private GalleryViewModel? vm;
    private CollectionRepository collections = null!;
    private int keyCollectionId;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 8; i++)
        {
            products.Add(new Product
            {
                Model = $"cc.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
                RandomKey = i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);

        // 星标收藏：001/002；合集成员：003/004（与星标独立）。Id 为自增，按型号回查真实 Id。
        await using var readContext = host.CreateContext();
        var idOf = readContext.Products.AsNoTracking()
            .ToDictionary(p => p.Model, p => p.Id);
        var favorites = new FavoritesRepository(host.CreateContext());
        await favorites.AddAsync(idOf["cc.model.001"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await favorites.AddAsync(idOf["cc.model.002"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        collections = new CollectionRepository(host.CreateContext());
        keyCollectionId = (await collections.CreateAsync("重点", DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Id;
        await collections.AddItemAsync(keyCollectionId, idOf["cc.model.003"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(keyCollectionId, idOf["cc.model.004"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
            debounceMilliseconds: 10,
            collections: new CollectionRepository(host.CreateContext()));
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
            $"等待超时 [Cards={v.Cards.Count} State={v.State} Summary={v.ResultSummary} Collections={v.Collections.Count}]");
    }

    [Fact]
    public async Task EnterFavorites_LoadsCollections_DefaultSelected()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 2);
        Assert.Equal(CollectionOption.Default, vm.SelectedCollection);
        await WaitForAsync(() => vm.Cards.Count == 2);
        Assert.All(vm.Cards, card => Assert.Contains(card.Model, new[] { "cc.model.001", "cc.model.002" }));
    }

    [Fact]
    public async Task SelectCollection_ShowsOnlyMembers_IndependentOfFavorites()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 2 && vm.Cards.Count == 2);

        vm.SelectedCollection = new CollectionOption(keyCollectionId, "重点");
        await WaitForAsync(() => vm.Cards.Count == 2
            && vm.Cards.Any(card => card.Model == "cc.model.003"));
        Assert.All(vm.Cards, card => Assert.Contains(card.Model, new[] { "cc.model.003", "cc.model.004" }));
        Assert.Equal(2, vm.TotalCount);
        Assert.Contains("重点", vm.ResultSummary);
    }

    [Fact]
    public async Task ChipRemove_Collection_ReturnsToDefault()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 2 && vm.Cards.Count == 2);
        vm.SelectedCollection = new CollectionOption(keyCollectionId, "重点");
        await WaitForAsync(() => vm.Cards.Any(card => card.Model == "cc.model.003"));

        vm.RemoveChip("collection");
        await WaitForAsync(() => vm.Cards.Count == 2
            && vm.Cards.All(card => card.Model is "cc.model.001" or "cc.model.002"));
        Assert.Equal(CollectionOption.Default, vm.SelectedCollection);
    }

    [Fact]
    public async Task DeleteSelectedCollection_FallsBackToDefault()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 2 && vm.Cards.Count == 2);
        vm.SelectedCollection = new CollectionOption(keyCollectionId, "重点");
        await WaitForAsync(() => vm.Cards.Any(card => card.Model == "cc.model.003"));

        await collections.DeleteAsync(keyCollectionId);
        await vm.ReloadCollectionsAsync();
        await WaitForAsync(() => vm.Collections.Count == 1
            && vm.SelectedCollection == CollectionOption.Default
            && vm.Cards.Count == 2
            && vm.Cards.All(card => card.Model is "cc.model.001" or "cc.model.002"));
    }
}
