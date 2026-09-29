using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 收藏夹查询：CollectionId 过滤只返回合集成员（数据库侧子查询），
/// 与关键字/分页组合语义正确，空合集总数为 0。
/// </summary>
public sealed class CollectionQueryTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private int collectionId;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 10; i++)
        {
            products.Add(new Product
            {
                Model = $"col.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);

        // Id 为自增，按型号回查真实 Id（成员 003/004；重复加入一条验证幂等）。
        await using var readContext = host.CreateContext();
        var idOf = readContext.Products.AsNoTracking()
            .ToDictionary(p => p.Model, p => p.Id);
        var collections = new CollectionRepository(host.CreateContext());
        var collection = await collections.CreateAsync("重点设备", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        collectionId = collection.Id;
        await collections.AddItemAsync(collectionId, idOf["col.model.003"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(collectionId, idOf["col.model.004"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await collections.AddItemAsync(collectionId, idOf["col.model.003"], DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private ProductQueryService CreateService()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new ProductQueryService(factory, new FilterService(), new SortService());
    }

    [Fact]
    public async Task CollectionFilter_ReturnsOnlyMembers_Deduplicated()
    {
        var page = await CreateService().QueryPageAsync(
            new ProductQuery { CollectionId = collectionId },
            0,
            21,
            cancellationToken: default);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["col.model.003", "col.model.004"], page.Items.Select(p => p.Model));
    }

    [Fact]
    public async Task CollectionFilter_CombinesWithKeyword_AndPaging()
    {
        var page = await CreateService().QueryPageAsync(
            new ProductQuery { CollectionId = collectionId, Keyword = "产品 00" },
            0,
            1,
            cancellationToken: default);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("col.model.003", page.Items[0].Model);
    }

    [Fact]
    public async Task CollectionFilter_EmptyCollection_TotalZero()
    {
        var collections = new CollectionRepository(host.CreateContext());
        var empty = await collections.CreateAsync("空合集", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var page = await CreateService().QueryPageAsync(
            new ProductQuery { CollectionId = empty.Id },
            0,
            21,
            cancellationToken: default);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task CollectionFilter_TranslatedToDatabaseSql()
    {
        await using var context = host.CreateContext();
        var sql = context.Products
            .Where(p => context.CollectionItems.Any(i => i.CollectionId == collectionId && i.ProductId == p.Id))
            .ToQueryString();
        Assert.Contains("CollectionItems", sql);
        Assert.Contains("EXISTS", sql.ToUpperInvariant());
    }
}
