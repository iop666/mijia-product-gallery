using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 性能基准测试：数据规模矩阵（默认 10k，设置 PERF_MATRIX=1 跑 1k/5k/10k/30k 全矩阵）。
/// 指标采集 + 输出报告；软阈值断言防退化，FPS/滚动流畅度不在断言范围。
/// </summary>
[Trait("Category", "Performance")]
public sealed class PerformanceBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly List<DatabaseTestHost> hosts = [];

    public PerformanceBenchmarkTests(ITestOutputHelper testOutputHelper)
    {
        output = testOutputHelper;
    }

    public void Dispose()
    {
        foreach (var h in hosts)
        {
            h.Dispose();
        }
    }

    private PerfScaleHost CreateSeededHost(int count)
    {
        var host = new PerfScaleHost(count);
        SeedProducts(host.Host, count);
        return host;
    }

    private static void SeedProducts(DatabaseTestHost host, int count)
    {
        host.Paths.EnsureDirectories();
        using var context = host.CreateContext();
        context.Database.EnsureCreated();
        var brands = new[] { "小米出品", "智米", "石头", "第三方品牌" };
        var categories = new[] { "环境电器", "个护与起居", "厨房电器", "安防" };
        var rows = new List<MijiaProductGallery.Core.Models.Product>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new MijiaProductGallery.Core.Models.Product
            {
                Model = $"perf.model.{i:00000}",
                Name = MakeName(i),
                Brand = brands[i % brands.Length],
                Category = categories[i % categories.Length],
                ImageFileName = $"perf.model.{i:00000}.png",
                ImageFormat = ImageFormat.Png,
                Sha256 = $"perfsha256{i:0000000}",
                IsAvailable = i % 20 != 0,
                UpdateTimeUnix = 1_700_000_000 + i,
                FirstSeenUnix = 1_600_000_000 + i,
                LastSeenUnix = 1_800_000_000,
                RandomKey = i,
            });
        }

        foreach (var batch in rows.Chunk(5_000))
        {
            context.AddRange(batch);
            context.SaveChanges();
        }

        // 使用计数：前 min(1000, count/10) 个产品各 1~10 次，供使用次数/最近使用排序。
        var usageRows = new List<MijiaProductGallery.Core.Models.ProductUsage>();
        var eventRows = new List<MijiaProductGallery.Core.Models.UsageEvent>();
        var usageCount = Math.Min(1_000, count / 10);
        for (var i = 0; i < usageCount; i++)
        {
            var total = (i % 10) + 1;
            usageRows.Add(new MijiaProductGallery.Core.Models.ProductUsage
            {
                ProductId = rows[i].Id,
                TotalUseCount = total,
                CopyCount = total,
                LastUsedUnix = 1_700_000_000 + i,
            });
            eventRows.Add(new MijiaProductGallery.Core.Models.UsageEvent
            {
                ProductId = rows[i].Id,
                Type = MijiaProductGallery.Core.Enums.UsageType.Copy,
                UsedUnix = 1_700_000_000 + i,
            });
        }

        foreach (var batch in usageRows.Chunk(5_000))
        {
            context.AddRange(batch);
        }

        foreach (var batch in eventRows.Chunk(5_000))
        {
            context.AddRange(batch);
        }

        context.SaveChanges();
    }

    private static string MakeName(int i)
    {
        if (i % 25 == 0)
        {
            return $"perf 空气产品 {i:00000}";
        }

        if (i % 50 == 0)
        {
            return $"perf 小米产品 {i:00000}";
        }

        if (i % 100 == 7)
        {
            return $"perf camera {i:00000}";
        }

        return $"perf 产品 {i:00000}";
    }

    public static IEnumerable<object[]> ScaleData()
    {
        yield return [1_000];
        yield return [5_000];
        yield return [10_000];
        yield return [30_000];
    }

    [Fact]
    public async Task QueryBenchmark_Search_AtMatrixScales()
    {
        var fullMatrix = Environment.GetEnvironmentVariable("PERF_MATRIX") == "1";
        var scales = fullMatrix ? new[] { 1_000, 5_000, 10_000, 30_000 } : new[] { 10_000 };
        foreach (var scale in scales)
        {
            using PerfScaleHost host = CreateSeededHost(scale);
            var factory = new TestDbContextFactory(host.CreateContext);
            var queryService = new ProductQueryService(factory, new FilterService(), new SortService());
            // 预热：吸收进程级模型编译与首个查询形状的编译成本，避免计入冷启动噪声。
            await queryService.QueryAsync(new ProductQuery { Keyword = "warmup" });
            var stopwatch = Stopwatch.StartNew();
            var products = new ProductRepository(host.CreateContext());
            var hasProducts = await products.CountAsync() >= scale;
            stopwatch.Stop();
            output.WriteLine($"[seed {scale}] 完成，count 校验 {hasProducts}，{stopwatch.ElapsedMilliseconds}ms");

            foreach (var keyword in new[] { "空气", "小米", "camera", "zzz-no-match" })
            {
                stopwatch.Restart();
                var hits = await queryService.QueryAsync(new ProductQuery { Keyword = keyword });
                stopwatch.Stop();
                output.WriteLine($"[query {scale}] '{keyword}' → {hits.Count} 行，{stopwatch.Elapsed.TotalMilliseconds:F1}ms");
                if (scale <= 10_000)
                {
                    Assert.True(stopwatch.ElapsedMilliseconds < 100, $"{keyword}@{scale} 超过 100ms");
                }
            }
        }
    }

    [Fact]
    public async Task FilterBenchmark_Combined_AtMatrixScales()
    {
        var fullMatrix = Environment.GetEnvironmentVariable("PERF_MATRIX") == "1";
        var scales = fullMatrix ? new[] { 1_000, 5_000, 10_000, 30_000 } : new[] { 10_000 };
        foreach (var scale in scales)
        {
            using PerfScaleHost host = CreateSeededHost(scale);
            var factory = new TestDbContextFactory(host.CreateContext);
            var queryService = new ProductQueryService(factory, new FilterService(), new SortService());

            var filter = new ProductFilter
            {
                Brands = ["小米出品"],
                Categories = ["环境电器"],
                HasImage = true,
                IsAvailable = true,
            };
            // 预热与被测相同的查询形状，剔除 EF 首次形状编译的一次性成本。
            await queryService.QueryAsync(new ProductQuery { Filter = filter.Clone() });
            var stopwatch = Stopwatch.StartNew();
            var hits = await queryService.QueryAsync(new ProductQuery { Filter = filter });
            stopwatch.Stop();
            output.WriteLine($"[filter {scale}] 组合筛选 → {hits.Count} 行，{stopwatch.Elapsed.TotalMilliseconds:F1}ms");
            if (scale <= 10_000)
            {
                Assert.True(stopwatch.ElapsedMilliseconds < 100, $"筛选@{scale} 超过 100ms");
            }

            filter.Usage = UsageRange.Used;
            await queryService.QueryAsync(new ProductQuery { Filter = filter.Clone() });
            stopwatch.Restart();
            hits = await queryService.QueryAsync(new ProductQuery { Filter = filter });
            stopwatch.Stop();
            output.WriteLine($"[filter+usage {scale}] → {hits.Count} 行，{stopwatch.Elapsed.TotalMilliseconds:F1}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 300, $"使用筛选@{scale} 超过 300ms");
        }
    }

    [Fact]
    public async Task SortBenchmark_SixSorts_AtMatrixScales()
    {
        var fullMatrix = Environment.GetEnvironmentVariable("PERF_MATRIX") == "1";
        var scales = fullMatrix ? new[] { 1_000, 5_000, 10_000, 30_000 } : new[] { 10_000 };
        foreach (var scale in scales)
        {
            using PerfScaleHost host = CreateSeededHost(scale);
            var factory = new TestDbContextFactory(host.CreateContext);
            var queryService = new ProductQueryService(factory, new FilterService(), new SortService());
            // 预热后再计时。
            await queryService.QueryAsync(new ProductQuery
            {
                Sort = new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Ascending },
            });
            var fields = new[]
            {
                (ProductSortField.Model, false),
                (ProductSortField.Model, true),
                (ProductSortField.Name, false),
                (ProductSortField.Name, true),
                (ProductSortField.UsageCount, true),
                (ProductSortField.LastUsed, false),
                (ProductSortField.UpdateTime, true),
                (ProductSortField.AddedTime, false),
            };
            foreach (var (field, descending) in fields)
            {
                var stopwatch = Stopwatch.StartNew();
                var rows = await queryService.QueryAsync(new ProductQuery
                {
                    Sort = new ProductSort { Field = field, Direction = descending ? SortDirection.Descending : SortDirection.Ascending },
                });
                stopwatch.Stop();
                output.WriteLine($"[sort {scale}] {field}{(descending ? " DESC" : " ASC")} → {rows.Count} 行，{stopwatch.Elapsed.TotalMilliseconds:F1}ms");
                Assert.Equal(scale, rows.Count);
                if (scale <= 10_000)
                {
                    Assert.True(stopwatch.ElapsedMilliseconds < 300, $"{field} 排序@{scale} 超过 300ms");
                }
            }
        }
    }

    [Fact]
    public async Task MemorySmokeTest_10k_LoadAndQueries()
    {
        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        output.WriteLine($"[memory] before: {workingSetBefore / 1024 / 1024}MB");

        using PerfScaleHost host = CreateSeededHost(10_000);
        var factory = new TestDbContextFactory(() => host.CreateContext());
        var queryService = new ProductQueryService(factory, new FilterService(), new SortService());

        // 全量加载 + 组合查询 + 排序各跑一轮。
        var all = await queryService.QueryAsync(new ProductQuery());
        var search = await queryService.QueryAsync(new ProductQuery { Keyword = "perf" });
        var sorted = await queryService.QueryAsync(new ProductQuery
        {
            Sort = new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Descending },
        });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        output.WriteLine($"[memory] after 10k 全量+搜索+排序: {workingSetAfter / 1024 / 1024}MB (rows {all.Count}/{search.Count}/{sorted.Count})");

        Assert.Equal(10_000, all.Count);
        Assert.True(workingSetAfter < 800L * 1024 * 1024, $"10k 全量加载后工作集 {workingSetAfter / 1024 / 1024}MB 超过 800MB");
    }

    [Fact]
    public async Task StartupBenchmark_Initialize_AndGalleryReady()
    {
        // 空库初始化时间。
        using var emptyHost = DatabaseTestHost.CreateNotInitialized();
        var stopwatch = Stopwatch.StartNew();
        await using (var context = emptyHost.CreateContext())
        {
            await new DbInitializer(context, emptyHost.Paths).InitializeAsync();
        }

        stopwatch.Stop();
        output.WriteLine($"[startup] 空库初始化 {stopwatch.ElapsedMilliseconds}ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 1_000, "空库初始化超过 1s");

        // 30k 库"图库就绪"：先按首启路径迁移建库，再灌数据，最后测打开+全量行加载。
        using PerfScaleHost host = new PerfScaleHost(30_000);
        await using (var context = host.CreateContext())
        {
            await new DbInitializer(context, host.Paths).InitializeAsync();
        }

        SeedProducts(host.Host, 30_000);
        GC.Collect();
        stopwatch.Restart();
        await using (var context = host.CreateContext())
        {
            await new DbInitializer(context, host.Paths).InitializeAsync();
            var rows = await context.Products.AsNoTracking().ToListAsync();
            Assert.Equal(30_000, rows.Count);
        }

        stopwatch.Stop();
        output.WriteLine($"[startup] 30k 库打开+全量行加载 {stopwatch.ElapsedMilliseconds}ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 5_000, "30k 库就绪超过 5s");
    }
}

/// <summary>数据规模矩阵宿主：每个规模独立临时库，转发常用成员便于测试书写。</summary>
public sealed class PerfScaleHost : IDisposable
{
    public PerfScaleHost(int count)
    {
        Host = DatabaseTestHost.CreateNotInitialized();
        Count = count;
    }

    public DatabaseTestHost Host { get; }

    public int Count { get; }

    public GalleryDbContext CreateContext() => Host.CreateContext();

    public DatabasePaths Paths => Host.Paths;

    public void Dispose()
    {
        Host.Dispose();
    }
}
