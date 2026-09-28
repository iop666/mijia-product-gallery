using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>随机浏览查询测试：游标抽取、排除已展示、不足回卷、筛选/搜索照常生效。</summary>
public sealed class RandomBrowseQueryTests : IAsyncLifetime
{
    private const int BatchLimit = 20;

    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new ProductRepository(context);
        var rows = new List<Product>();
        for (var i = 0; i < 45; i++)
        {
            rows.Add(new Product
            {
                Model = $"model.{i:00}",
                Name = $"产品 {i}",
                Brand = i % 2 == 0 ? "品牌甲" : "品牌乙",
                Category = i < 40 ? "环境电器" : "厨房电器",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
                RandomKey = Random.Shared.NextInt64(),
            });
        }

        await products.AddRangeAsync(rows);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private ProductQueryService CreateQueryService()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new ProductQueryService(factory, new FilterService(), new SortService());
    }

    private static ProductQuery RandomQuery(
        long? cursor = null,
        IReadOnlyList<string>? exclude = null,
        string? keyword = null,
        ProductFilter? filter = null)
    {
        return new ProductQuery
        {
            Keyword = keyword,
            Filter = filter,
            Mode = BrowseMode.Random,
            RandomCursor = cursor,
            RandomLimit = BatchLimit,
            ExcludeModels = exclude,
        };
    }

    [Fact]
    public async Task RandomBatch_RespectsLimit_AndSortsByRandomKey()
    {
        var first = await CreateQueryService().QueryAsync(RandomQuery());

        Assert.Equal(BatchLimit, first.Count);
        var keys = first.Select(product => product.RandomKey!.Value).ToList();
        Assert.Equal(keys.OrderBy(key => key).ToList(), keys);
    }

    [Fact]
    public async Task SequentialBatches_NoDuplicates()
    {
        var service = CreateQueryService();
        var shown = new List<string>();
        long? cursor = null;
        var all = new List<Product>();

        // 45 个产品：两整批（20+20）+ 第三批补齐剩余 5 个，全程无重复。
        for (var round = 0; round < 3; round++)
        {
            IReadOnlyList<Product> batch = await service.QueryAsync(RandomQuery(cursor, shown));
            Assert.All(batch, product => Assert.DoesNotContain(product.Model, shown));
            all.AddRange(batch);
            shown.AddRange(batch.Select(product => product.Model));
            cursor = batch.Count == 0 ? cursor : batch.Max(product => product.RandomKey!.Value);
        }

        Assert.Equal(45, all.Count);
        Assert.Equal(45, all.Select(product => product.Model).Distinct().Count());
    }

    [Fact]
    public async Task InsufficientAfterCursor_WrapsAroundFromStart()
    {
        var service = CreateQueryService();
        IReadOnlyList<Product> first = await service.QueryAsync(RandomQuery());
        var cursor = first.Max(product => product.RandomKey!.Value);
        var exclude = first.Select(product => product.Model).ToList();

        // 游标之后仅剩 45 - 20 = 25 个中的一部分？45 个产品，游标后可能少于 20——
        // 断言第二批与第一批无交集，且总数不重复。
        IReadOnlyList<Product> second = await service.QueryAsync(RandomQuery(cursor, exclude));

        Assert.All(second, product => Assert.DoesNotContain(product.Model, exclude));
        Assert.True(second.Count > 0 || 45 - first.Count == 0);
    }

    [Fact]
    public async Task RandomMode_RespectsFilter()
    {
        var results = await CreateQueryService().QueryAsync(RandomQuery(filter: new ProductFilter { Categories = ["厨房电器"] }));

        Assert.All(results, product => Assert.Equal("厨房电器", product.Category));
        Assert.True(results.Count <= BatchLimit);
    }

    [Fact]
    public async Task RandomMode_RespectsKeyword()
    {
        var results = await CreateQueryService().QueryAsync(RandomQuery(keyword: "产品 4"));

        Assert.NotEmpty(results);
        Assert.All(results, product => Assert.Contains("产品 4", product.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsEmpty()
    {
        var emptyHost = new DatabaseTestHost();
        try
        {
            await using var context = emptyHost.CreateContext();
            await new DbInitializer(context, emptyHost.Paths).InitializeAsync();
            var factory = new TestDbContextFactory(() => emptyHost.CreateContext());
            var service = new ProductQueryService(factory, new FilterService(), new SortService());

            var results = await service.QueryAsync(RandomQuery());

            Assert.Empty(results);
        }
        finally
        {
            emptyHost.Dispose();
        }
    }

    [Fact]
    public async Task ExhaustedSession_ReturnsEmpty_WhenAllShown()
    {
        var service = CreateQueryService();
        var shown = new List<string>();
        long? cursor = null;
        IReadOnlyList<Product> batch;
        var rounds = 0;

        do
        {
            batch = await service.QueryAsync(RandomQuery(cursor, shown));
            shown.AddRange(batch.Select(product => product.Model));
            cursor = batch.Count == 0 ? cursor : batch.Max(product => product.RandomKey!.Value);
            rounds++;
            Assert.True(rounds <= 10, "回卷未终止");
        }
        while (batch.Count > 0);

        Assert.Equal(45, shown.Count);
    }
}

/// <summary>随机键回填测试：历史行（RandomKey 为 null）在初始化后被补齐。</summary>
public sealed class RandomKeyBackfillTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initialize_BackfillsNullRandomKeys()
    {
        // 先建库并插入 RandomKey 为 null 的行（模拟历史数据）。
        await using (var context = host.CreateContext())
        {
            await new DbInitializer(context, host.Paths).InitializeAsync();
            await new ProductRepository(context).AddAsync(new Product
            {
                Model = "legacy.model.01",
                Name = "历史产品",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            });
        }

        // 再次初始化触发回填。
        await using (var context = host.CreateContext())
        {
            await new DbInitializer(context, host.Paths).InitializeAsync();
        }

        await using var verify = host.CreateContext();
        var product = await verify.Products.AsNoTracking().SingleAsync(p => p.Model == "legacy.model.01");
        Assert.NotNull(product.RandomKey);
    }
}

/// <summary>随机浏览测试宿主。</summary>
public sealed class Host : DatabaseTestHost
{
}
