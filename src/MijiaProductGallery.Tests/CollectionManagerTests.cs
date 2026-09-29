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
/// 收藏夹管理规则：新建（空名/重名拒绝、成功后自动选中）、重命名（默认收藏拒绝、
/// 空名/重名拒绝）、删除（默认收藏拒绝；只删合集本身，产品与星标收藏不受影响）、
/// 多合集成员与移除、取消收藏、收藏视图空状态。
/// </summary>
public sealed class CollectionManagerTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();
    private GalleryViewModel? vm;
    private CollectionRepository collections = null!;
    private FavoritesRepository favorites = null!;
    private int keyId;
    private int backupId;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 8; i++)
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

        await using var readContext = host.CreateContext();
        var idOf = readContext.Products.AsNoTracking()
            .ToDictionary(p => p.Model, p => p.Id);

        favorites = new FavoritesRepository(host.CreateContext());
        await favorites.AddAsync(idOf["cm.model.001"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await favorites.AddAsync(idOf["cm.model.002"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        collections = new CollectionRepository(host.CreateContext());
        keyId = (await collections.CreateAsync("重点", DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Id;
        backupId = (await collections.CreateAsync("备用", DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Id;
        await collections.AddItemAsync(keyId, idOf["cm.model.003"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(keyId, idOf["cm.model.004"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(backupId, idOf["cm.model.003"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(backupId, idOf["cm.model.005"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
            $"等待超时 [Cards={v.Cards.Count} State={v.State} Total={v.TotalCount} Collections={v.Collections.Count}]");
    }

    [Fact]
    public async Task CreateCollection_EmptyName_Fails()
    {
        var (success, error) = await vm!.CreateCollectionAsync("   ");
        Assert.False(success);
        Assert.Contains("不能为空", error);
    }

    [Fact]
    public async Task CreateCollection_DuplicateName_Fails()
    {
        var (success, error) = await vm!.CreateCollectionAsync("重点");
        Assert.False(success);
        Assert.Contains("同名", error);
    }

    [Fact]
    public async Task CreateCollection_Success_AutoSelected_AndShowsMembers()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 3 && vm.TotalCount == 2);

        var (success, error) = await vm.CreateCollectionAsync("新合集");
        Assert.True(success, error);
        await WaitForAsync(() => vm.SelectedCollection is { Id: > 0, Name: "新合集" });
        await WaitForAsync(() => vm.TotalCount == 0 && vm.IsFavoritesEmptyVisible);
        Assert.Contains("新合集", vm.FavoritesEmptyText);
        Assert.Equal(["默认收藏（星标）", "重点", "备用", "新合集"],
            vm.Collections.Select(option => option.Name));
    }

    [Fact]
    public async Task RenameCollection_Default_Fails()
    {
        var (success, error) = await vm!.RenameCollectionAsync(0, "改名");
        Assert.False(success);
        Assert.Contains("默认收藏不可重命名", error);
    }

    [Fact]
    public async Task RenameCollection_EmptyName_Fails()
    {
        var (success, error) = await vm!.RenameCollectionAsync(keyId, " ");
        Assert.False(success);
        Assert.Contains("不能为空", error);
    }

    [Fact]
    public async Task RenameCollection_DuplicateName_Fails()
    {
        var (success, error) = await vm!.RenameCollectionAsync(keyId, "备用");
        Assert.False(success);
        Assert.Contains("同名", error);
    }

    [Fact]
    public async Task RenameCollection_Success_UpdatesList()
    {
        var (success, error) = await vm!.RenameCollectionAsync(keyId, "核心");
        Assert.True(success, error);
        await WaitForAsync(() => vm.Collections.Any(option => option.Name == "核心"));
        var rows = await collections.GetAllAsync();
        Assert.Equal("核心", rows.Single(row => row.Id == keyId).Name);
    }

    [Fact]
    public async Task DeleteCollection_Default_Fails()
    {
        var (success, error) = await vm!.DeleteCollectionAsync(0);
        Assert.False(success);
        Assert.Contains("默认收藏不可删除", error);
    }

    [Fact]
    public async Task DeleteCollection_Success_ProductsAndFavoritesUntouched()
    {
        await using var before = host.CreateContext();
        var productCountBefore = await before.Products.CountAsync();
        var favoritesBefore = await favorites.GetFavoriteProductIdsAsync();

        var (success, error) = await vm!.DeleteCollectionAsync(keyId);
        Assert.True(success, error);
        await WaitForAsync(() => vm.Collections.All(option => option.Id != keyId));

        await using var after = host.CreateContext();
        Assert.Equal(productCountBefore, await after.Products.CountAsync());
        Assert.Equal(favoritesBefore, await favorites.GetFavoriteProductIdsAsync());
        var leftover = await after.CollectionItems
            .Where(item => item.CollectionId == keyId)
            .ToListAsync();
        Assert.Empty(leftover);
    }

    [Fact]
    public async Task Product_CanJoinMultipleCollections()
    {
        await using var context = host.CreateContext();
        var productId = await context.Products.AsNoTracking()
            .Where(p => p.Model == "cm.model.003")
            .Select(p => p.Id)
            .SingleAsync();
        var inKey = (await collections.GetProductIdsAsync(keyId)).Contains(productId);
        var inBackup = (await collections.GetProductIdsAsync(backupId)).Contains(productId);
        Assert.True(inKey && inBackup);
    }

    [Fact]
    public async Task RemoveFromCollection_OnlyThatCollection()
    {
        await using var context = host.CreateContext();
        var productId = await context.Products.AsNoTracking()
            .Where(p => p.Model == "cm.model.003")
            .Select(p => p.Id)
            .SingleAsync();
        var favoritesBefore = await favorites.GetFavoriteProductIdsAsync();

        await collections.RemoveItemAsync(keyId, productId);

        Assert.DoesNotContain(productId, await collections.GetProductIdsAsync(keyId));
        Assert.Contains(productId, await collections.GetProductIdsAsync(backupId));
        Assert.Equal(favoritesBefore, await favorites.GetFavoriteProductIdsAsync());
    }

    [Fact]
    public async Task Unfavorite_RemovesFromFavorites()
    {
        await using var context = host.CreateContext();
        var product = await context.Products.AsNoTracking()
            .SingleAsync(p => p.Model == "cm.model.001");

        var newState = await vm!.ToggleFavoriteAsync(new ProductCard(product));

        Assert.False(newState);
        Assert.DoesNotContain(product.Id, await favorites.GetFavoriteProductIdsAsync());
    }

    [Fact]
    public async Task FavoritesEmpty_ClearsWhenKeywordActive()
    {
        vm!.EnterFavoritesMode();
        await WaitForAsync(() => vm.Collections.Count == 3 && vm.TotalCount == 2);
        Assert.False(vm.IsFavoritesEmptyVisible);

        vm.SelectedCollection = new CollectionOption(backupId, "备用");
        await WaitForAsync(() => vm.SelectedCollection?.Id == backupId && vm.TotalCount == 2);

        // 建一个空合集并切换过去 → 空状态出现。
        var (success, _) = await vm.CreateCollectionAsync("空合集");
        Assert.True(success);
        await WaitForAsync(() => vm.IsFavoritesEmptyVisible);

        vm.ApplySearchImmediate("产品");
        await WaitForAsync(() => !vm.IsFavoritesEmptyVisible || vm.TotalCount > 0);
        Assert.False(vm.IsFavoritesEmptyVisible);
    }
}
