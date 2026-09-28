using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 统计服务测试：今日/本周/本月/累计边界与 Top 产品（FixedTimeProvider 固定时钟）。
/// </summary>
public sealed class UsageStatisticsServiceTests : IAsyncLifetime
{
    // 固定"当前时间"：2026-09-29（周二）20:00 本地（UTC+8）→ UTC 12:00。
    private static readonly DateTimeOffset FixedNow =
        new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private UsageStatisticsService service = null!;

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        service = new UsageStatisticsService(
            new TestDbContextFactory(() => host.CreateContext()),
            new FixedTimeProvider(FixedNow));
    }

    private async Task RecordEventAsync(long unix)
    {
        var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product
        {
            Model = $"m-{unix}",
            Name = $"产品 {unix}",
            Brand = "b",
            Category = "c",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        };
        await products.AddAsync(product);
        var usageRepository = new UsageRepository(context);
        await usageRepository.RecordAsync(product.Id, UsageType.Copy, unix);
    }

    [Fact]
    public async Task EmptyDatabase_AllZeros_EmptyTop()
    {
        var statistics = await service.GetStatisticsAsync();

        Assert.Equal(0, statistics.TodayCount);
        Assert.Equal(0, statistics.WeekCount);
        Assert.Equal(0, statistics.MonthCount);
        Assert.Equal(0, statistics.TotalCount);
        Assert.Empty(statistics.TopProducts);
    }

    [Fact]
    public async Task Buckets_CountByLocalDayWeekMonth()
    {
        var localNow = FixedNow.ToLocalTime();
        // 今日（本地 09-29 内）。
        await RecordEventAsync(UnixOf(localNow.Date.AddHours(9)));
        // 本周更早（本地周一 09-28 上午）。
        await RecordEventAsync(UnixOf(localNow.Date.AddDays(-1).AddHours(9)));
        // 本月更早（本地 09-01）。
        await RecordEventAsync(UnixOf(new DateTimeOffset(localNow.Year, localNow.Month, 1, 9, 0, 0, localNow.Offset)));
        // 上月（必然在月界之外）。
        await RecordEventAsync(UnixOf(new DateTimeOffset(localNow.Year, localNow.Month, 1, 9, 0, 0, localNow.Offset).AddDays(-5)));

        var statistics = await service.GetStatisticsAsync();

        Assert.Equal(1, statistics.TodayCount);
        Assert.Equal(2, statistics.WeekCount);
        Assert.Equal(3, statistics.MonthCount);
        Assert.Equal(4, statistics.TotalCount);
    }

    [Fact]
    public async Task Event_ExactlyAtLocalMidnight_CountsAsToday()
    {
        var localNow = FixedNow.ToLocalTime();
        await RecordEventAsync(UnixOf(localNow.Date));

        var statistics = await service.GetStatisticsAsync();

        Assert.Equal(1, statistics.TodayCount);
    }

    [Fact]
    public async Task Total_ComesFromUsagesSum_NotEventCount()
    {
        // 同一产品累计 3 次（事件 3 条）→ Total=3；清空事件后 Total 仍为 3。
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product { Model = "a.model.01", Name = "甲", Brand = "b", Category = "c", FirstSeenUnix = 1, LastSeenUnix = 1 };
        await products.AddAsync(product);
        var usageRepository = new UsageRepository(context);
        for (var i = 0; i < 3; i++)
        {
            await usageRepository.RecordAsync(product.Id, UsageType.Copy, 1_700_000_000 + i);
        }

        Assert.Equal(3, (await service.GetStatisticsAsync()).TotalCount);

        await new RecentService(host.CreateContext()).ClearHistoryAsync();

        Assert.Equal(0, (await service.GetStatisticsAsync()).TotalCount);
    }

    [Fact]
    public async Task TopProducts_OrdersDescending_ExcludesZero_RespectsLimit()
    {
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        var rows = new List<Product>();
        // b=12, d=3, a=1, e/c=0（不进榜）。
        var usagePlan = new Dictionary<string, int>
        {
            ["m-a"] = 1,
            ["m-b"] = 13,
            ["m-c"] = 0,
            ["m-d"] = 3,
            ["m-e"] = 0,
        };
        foreach (var (model, count) in usagePlan)
        {
            var product = new Product { Model = model, Name = $"名称 {model}", Brand = "b", Category = "c", FirstSeenUnix = 1, LastSeenUnix = 1 };
            rows.Add(product);
        }

        await products.AddRangeAsync(rows);
        var usageRepository = new UsageRepository(context);
        foreach (var row in rows)
        {
            for (var i = 0; i < usagePlan[row.Model]; i++)
            {
                await usageRepository.RecordAsync(row.Id, UsageType.Copy, 1_700_000_000);
            }
        }

        var statistics = await service.GetStatisticsAsync(topCount: 2);

        Assert.Equal(2, statistics.TopProducts.Count);
        Assert.Equal("m-b", statistics.TopProducts[0].Model);
        Assert.Equal(13, statistics.TopProducts[0].TotalUseCount);
        Assert.Equal("m-d", statistics.TopProducts[1].Model);
    }

    [Fact]
    public async Task ClearHistory_ZeroesAllBuckets()
    {
        var localNow = FixedNow.ToLocalTime();
        await RecordEventAsync(UnixOf(localNow.Date.AddHours(9)));
        await new RecentService(host.CreateContext()).ClearHistoryAsync();

        var statistics = await service.GetStatisticsAsync();

        Assert.Equal(0, statistics.TodayCount);
        Assert.Equal(0, statistics.TotalCount);
    }

    private static long UnixOf(DateTimeOffset local)
    {
        return local.ToUnixTimeSeconds();
    }
}
