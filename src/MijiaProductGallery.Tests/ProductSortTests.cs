using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>排序服务与组合管线测试：六族排序、方向、边界与 Search+Filter+Sort 组合（真实 SQLite）。</summary>
public sealed class ProductSortTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var store = new ImageStore(host.Paths);
        var image = await store.StoreNewAsync("m-a", new MemoryStream(ImageFixtures.CreatePng()));
        var products = new ProductRepository(context);
        await products.AddRangeAsync(
        [
            new Product
            {
                Model = "m-a",
                Name = "name-aa",
                Brand = "brand-x",
                Category = "环境电器",
                ImageFileName = image.ImageFileName,
                ImagePath = image.ImagePath,
                Sha256 = image.Sha256,
                UpdateTimeUnix = 100,
                FirstSeenUnix = 400,
            },
            new Product
            {
                Model = "m-b",
                Name = "name-bb",
                Brand = "brand-x",
                Category = "个护与起居",
                UpdateTimeUnix = 300,
                FirstSeenUnix = 100,
            },
            new Product
            {
                Model = "m-c",
                Name = "name-cc",
                Brand = "brand-y",
                Category = "环境电器",
                UpdateTimeUnix = 500,
                FirstSeenUnix = 300,
            },
            new Product
            {
                Model = "m-d",
                Name = "name-dd",
                Brand = "brand-y",
                Category = "安防",
                FirstSeenUnix = 500,
            },
            new Product
            {
                Model = "m-e",
                Name = "name-ee",
                Brand = "brand-x",
                Category = "厨房电器",
                UpdateTimeUnix = 200,
                FirstSeenUnix = 200,
            },
        ]);

        // 使用计数：a=1，b=12（高频），d=3；c/e 无记录（从未使用）。最近使用：a=1000，b=3000，d=2000；c/e 无。
        var usageRepository = new UsageRepository(context);
        var productA = (await products.GetByModelAsync("m-a"))!;
        var productB = (await products.GetByModelAsync("m-b"))!;
        var productD = (await products.GetByModelAsync("m-d"))!;
        await usageRepository.RecordAsync(productA.Id, UsageType.Copy, 1_000);
        await usageRepository.RecordAsync(productB.Id, UsageType.View, 3_000);
        for (var i = 0; i < 11; i++)
        {
            await usageRepository.RecordAsync(productB.Id, UsageType.View, 3_001 + i);
        }

        for (var i = 0; i < 3; i++)
        {
            await usageRepository.RecordAsync(productD.Id, UsageType.Drag, 2_000 + i);
        }
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

    private async Task<List<string>> QueryModelsAsync(ProductQuery query)
    {
        var rows = await CreateQueryService().QueryAsync(query);
        return rows.Select(product => product.Model).ToList();
    }

    [Fact]
    public async Task Model_Ascending_Default()
    {
        var models = await QueryModelsAsync(new ProductQuery());
        Assert.Equal(["m-a", "m-b", "m-c", "m-d", "m-e"], models);
    }

    [Fact]
    public async Task Model_Descending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-e", "m-d", "m-c", "m-b", "m-a"], models);
    }

    [Fact]
    public async Task Name_Ascending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Ascending },
        });
        Assert.Equal(["m-a", "m-b", "m-c", "m-d", "m-e"], models);
    }

    [Fact]
    public async Task Name_Descending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-e", "m-d", "m-c", "m-b", "m-a"], models);
    }

    [Fact]
    public async Task UsageCount_Ascending_CoalescesMissingToZero()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.UsageCount, Direction = SortDirection.Ascending },
        });
        // c/e 无记录按 0（型号序稳定），a=1，d=3，b=12。
        Assert.Equal(["m-c", "m-e", "m-a", "m-d", "m-b"], models);
    }

    [Fact]
    public async Task UsageCount_Descending_MostUsedFirst()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.UsageCount, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-b", "m-d", "m-a", "m-c", "m-e"], models);
    }

    [Fact]
    public async Task LastUsed_Ascending_NeverUsedFirst()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.LastUsed, Direction = SortDirection.Ascending },
        });
        // 无记录视为最低排最前（c/e），其后 a=1000、d=2000、b=3000。
        Assert.Equal(["m-c", "m-e", "m-a", "m-d", "m-b"], models);
    }

    [Fact]
    public async Task LastUsed_Descending_RecentFirst()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.LastUsed, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-b", "m-d", "m-a", "m-c", "m-e"], models);
    }

    [Fact]
    public async Task UpdateTime_Ascending_NullTreatedAsLowest()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.UpdateTime, Direction = SortDirection.Ascending },
        });
        // d 的 UpdateTimeUnix 为 null，按 0 处理排最前。
        Assert.Equal(["m-d", "m-a", "m-e", "m-b", "m-c"], models);
    }

    [Fact]
    public async Task UpdateTime_Descending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.UpdateTime, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-c", "m-b", "m-e", "m-a", "m-d"], models);
    }

    [Fact]
    public async Task AddedTime_Ascending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.AddedTime, Direction = SortDirection.Ascending },
        });
        Assert.Equal(["m-b", "m-e", "m-c", "m-a", "m-d"], models);
    }

    [Fact]
    public async Task AddedTime_Descending()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.AddedTime, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-d", "m-a", "m-c", "m-e", "m-b"], models);
    }

    [Fact]
    public async Task Pipeline_Keyword_Filter_UsageSort_Combined()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "name-",
            Filter = new ProductFilter { Categories = ["环境电器"] },
            Sort = new ProductSort { Field = ProductSortField.UsageCount, Direction = SortDirection.Descending },
        });

        // 关键字命中全部 5 个，大类筛出 a/c，使用次数降序：a(1) 在 c(0) 前。
        Assert.Equal(["m-a", "m-c"], models);
    }

    [Fact]
    public async Task Pipeline_Sort_OverridesSearchRank()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "name-",
            Filter = new ProductFilter { Categories = ["环境电器"] },
            Sort = new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Descending },
        });

        // 显式排序覆盖搜索命中优先级：环境电器内按型号降序。
        Assert.Equal(["m-c", "m-a"], models);
    }

    [Fact]
    public async Task Keyword_WithoutSort_KeepsSearchRank()
    {
        var models = await QueryModelsAsync(new ProductQuery { Keyword = "name-" });
        Assert.Equal(["m-a", "m-b", "m-c", "m-d", "m-e"], models);
    }

    [Fact]
    public async Task Sort_WithEmptyResult_ReturnsEmpty()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "zzz-no-match",
            Sort = new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending },
        });
        Assert.Empty(models);
    }

    [Fact]
    public async Task Keyword_Sort_NameDescending_OverridesRank()
    {
        // 有关键字 + 显式排序：排序覆盖搜索命中优先级。
        var models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "name-",
            Sort = new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Descending },
        });
        Assert.Equal(["m-e", "m-d", "m-c", "m-b", "m-a"], models);
    }

    [Fact]
    public async Task LastUsed_Ascending_Tiebreak_ByModel()
    {
        // c/e LastUsed 为 null（最低）并列时按型号次序稳定排序。
        var models = await QueryModelsAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.LastUsed, Direction = SortDirection.Ascending },
        });
        Assert.Equal(["m-c", "m-e", "m-a", "m-d", "m-b"], models);
    }
}

/// <summary>排序状态持久化与恢复测试。</summary>
public sealed class SortPersistenceTests
{
    private sealed class SortStateDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("field")]
        public string? Field { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("direction")]
        public string? Direction { get; set; }
    }

    [Fact]
    public void DefaultSort_IsModelAscending()
    {
        var sort = new ProductSort();
        Assert.Equal(ProductSortField.Model, sort.Field);
        Assert.Equal(SortDirection.Ascending, sort.Direction);
    }

    [Fact]
    public void SortStateDto_RoundTrips()
    {
        var dto = new SortStateDto { Field = "UsageCount", Direction = "Descending" };
        Assert.Equal("UsageCount", dto.Field);
        Assert.Equal("Descending", dto.Direction);
    }
}
