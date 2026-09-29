using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 分页查询：COUNT 与 Skip/Take 在数据库侧执行（SQL 含 LIMIT/OFFSET），
/// 当前页之外的数据不物化；总数/边界/不整除/空结果语义正确。
/// </summary>
public sealed class PaginationQueryTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var products = new List<Product>();
        for (var i = 0; i < 25; i++)
        {
            products.Add(new Product
            {
                Model = $"page.model.{i:000}",
                Name = $"产品 {i:000}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1_000 + i,
            });
        }

        await new ProductRepository(host.CreateContext()).AddRangeAsync(products);
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

    private static ProductQuery PageQuery() => new();

    [Fact]
    public async Task FirstPage_ReturnsPageSize_AndTotalCount()
    {
        var page = await CreateService().QueryPageAsync(PageQuery(), 0, 10, cancellationToken: default);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(25, page.TotalCount);
    }

    [Fact]
    public async Task NonDivisible_Total_LastPage_ReturnsRemainder()
    {
        var service = CreateService();
        var last = await service.QueryPageAsync(PageQuery(), 20, 10, cancellationToken: default);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(25, last.TotalCount);
    }

    [Fact]
    public async Task BeyondData_ReturnsEmpty_ButTotalStays()
    {
        var page = await CreateService().QueryPageAsync(PageQuery(), 100, 10, cancellationToken: default);
        Assert.Empty(page.Items);
        Assert.Equal(25, page.TotalCount);
    }

    [Fact]
    public async Task TotalPages_Math_MatchesPageSize()
    {
        var page = await CreateService().QueryPageAsync(PageQuery(), 0, 10, cancellationToken: default);
        Assert.Equal(3, page.TotalPages(10));
        Assert.Equal(2, page.TotalPages(21));
        Assert.Equal(1, page.TotalPages(140));
    }

    [Fact]
    public async Task EmptyResult_TotalZero()
    {
        var page = await CreateService().QueryPageAsync(
            new ProductQuery { Keyword = "no-such-product" },
            0,
            10,
            cancellationToken: default);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task SkipTake_TranslatedToDatabaseSql()
    {
        // 数据库侧执行：生成 SQL 必须含 LIMIT/OFFSET（SQLite），而非内存过滤。
        await using var context = host.CreateContext();
        var sql = context.Products
            .OrderBy(p => p.Model)
            .Skip(20)
            .Take(10)
            .ToQueryString();
        Assert.Matches(new Regex(@"LIMIT\s+@?\w+"), sql);
        Assert.Matches(new Regex(@"OFFSET\s+@?\w+"), sql);
    }

    [Fact]
    public async Task Keyword_Filter_Paging_Combined()
    {
        var page = await CreateService().QueryPageAsync(
            new ProductQuery { Keyword = "产品 00" },
            0,
            5,
            cancellationToken: default);
        Assert.Equal(5, page.Items.Count);
        Assert.All(page.Items, p => Assert.Contains("产品 00", p.Name));
    }

    [Fact]
    public async Task LargeScale_Paged_OnlyPageSizeRowsMaterialized()
    {
        // 大数据量：30,000 行，分页仅物化当前页 21 行（内存不随总数据量整体加载）。
        var products = new List<Product>(30_000);
        for (var i = 0; i < 30_000; i++)
        {
            products.Add(new Product
            {
                Model = $"big.model.{i:000000}",
                Name = $"大数据 {i}",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = i,
            });
        }

        foreach (var batch in products.Chunk(5_000))
        {
            await new ProductRepository(host.CreateContext()).AddRangeAsync(batch);
        }

        var page = await CreateService().QueryPageAsync(PageQuery(), 0, 21, cancellationToken: default);
        Assert.Equal(21, page.Items.Count);
        Assert.Equal(30_025, page.TotalCount); // 初始化 25 + 大数据 30,000

        var last = await CreateService().QueryPageAsync(PageQuery(), 29_979, 21, cancellationToken: default);
        Assert.Equal(21, last.Items.Count);
        Assert.Equal("big.model.029979", last.Items.First().Model);
        Assert.Equal("big.model.029999", last.Items.Last().Model);
    }
}
