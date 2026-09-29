using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 图库数据概览：产品总数 / 有图 / 无图 / 官网已移除 / 各大类数量全部来自 Products 真实数据，
/// 大类合计恒等于产品总数；清空使用历史不影响图库数量。
/// </summary>
public sealed class StatisticsOverviewTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private UsageStatisticsService service = null!;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        service = new UsageStatisticsService(new TestDbContextFactory(() => host.CreateContext()));
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Product> AddAsync(
        string model, string category, bool withImage = true, bool available = true)
    {
        var product = new Product
        {
            Model = model,
            Name = $"产品 {model}",
            Brand = "b",
            Category = category,
            FirstSeenUnix = 1_000,
            IsAvailable = available,
            ImageFileName = withImage ? $"{model}.png" : null,
            ImagePath = withImage ? $"images/{model}.png" : null,
            Sha256 = withImage ? $"sha-{model}" : null,
        };
        await new ProductRepository(host.CreateContext()).AddRangeAsync([product]);
        return product;
    }

    [Fact]
    public async Task EmptyDatabase_AllZero_NoCategories()
    {
        var overview = await service.GetGalleryOverviewAsync();
        Assert.Equal(0, overview.TotalCount);
        Assert.Equal(0, overview.WithImageCount);
        Assert.Equal(0, overview.WithoutImageCount);
        Assert.Equal(0, overview.RemovedCount);
        Assert.Empty(overview.Categories);
    }

    [Fact]
    public async Task SeededCategories_CountsFromRealData_SumEqualsTotal()
    {
        await AddAsync("m.env.001", "环境电器");
        await AddAsync("m.env.002", "环境电器");
        await AddAsync("m.kit.001", "厨房电器");
        await AddAsync("m.sec.001", "安防");

        var overview = await service.GetGalleryOverviewAsync();
        Assert.Equal(4, overview.TotalCount);
        Assert.Equal(
            new Dictionary<string, int> { ["环境电器"] = 2, ["厨房电器"] = 1, ["安防"] = 1 },
            overview.Categories.ToDictionary(c => c.Category, c => c.Count));
        Assert.Equal(overview.TotalCount, overview.Categories.Sum(c => c.Count));
    }

    [Fact]
    public async Task WithAndWithoutImage_SplitCoversTotal()
    {
        await AddAsync("m.a.001", "c1", withImage: true);
        await AddAsync("m.a.002", "c1", withImage: true);
        await AddAsync("m.a.003", "c1", withImage: false);

        var overview = await service.GetGalleryOverviewAsync();
        Assert.Equal(3, overview.TotalCount);
        Assert.Equal(2, overview.WithImageCount);
        Assert.Equal(1, overview.WithoutImageCount);
        Assert.Equal(overview.TotalCount, overview.WithImageCount + overview.WithoutImageCount);
    }

    [Fact]
    public async Task RemovedCount_CountsUnavailable_ButStaysInTotal()
    {
        await AddAsync("m.r.001", "c1", available: true);
        await AddAsync("m.r.002", "c1", available: false);

        var overview = await service.GetGalleryOverviewAsync();
        Assert.Equal(2, overview.TotalCount);
        Assert.Equal(1, overview.RemovedCount);
        // 下架属于状态维度：仍计入产品总数与所属大类。
        Assert.Equal(2, overview.Categories.Single(c => c.Category == "c1").Count);
    }

    [Fact]
    public async Task ClearHistory_DoesNotChangeGalleryOverview()
    {
        await AddAsync("m.h.001", "环境电器", withImage: false);
        var before = await service.GetGalleryOverviewAsync();

        await new RecentService(host.CreateContext()).ClearHistoryAsync();
        var after = await service.GetGalleryOverviewAsync();

        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.Categories, after.Categories);
    }

    [Fact]
    public async Task RealSeedShape_CategoryNames_AreActualData()
    {
        await AddAsync("m.x.001", "厨房电器", withImage: false);
        var overview = await service.GetGalleryOverviewAsync();
        // 大类名来自数据本身，不存在硬编码清单校验。
        Assert.Equal("厨房电器", overview.Categories.Single().Category);
        Assert.DoesNotContain(overview.Categories, c => c.Category == "不存在的大类");
    }
}
