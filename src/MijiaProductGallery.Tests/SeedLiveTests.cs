using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Seed;
using MijiaProductGallery.SeedPackTool;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 真实快照全量导入：默认直接返回（等效跳过）。
/// 手动执行：dotnet test -e SEED_LIVE=1 -e MIJIA_ICONS_DIR=&lt;mijia-product-icons 检出根&gt; --filter FullyQualifiedName~SeedLive
/// </summary>
public sealed class SeedLiveTests
{
    private readonly ITestOutputHelper output;

    public SeedLiveTests(ITestOutputHelper testOutputHelper)
    {
        output = testOutputHelper;
    }

    [Fact]
    public async Task BuildAndImport_20260928_RealSnapshot()
    {
        if (Environment.GetEnvironmentVariable("SEED_LIVE") != "1")
        {
            return;
        }

        var iconsDirectory = Environment.GetEnvironmentVariable("MIJIA_ICONS_DIR");
        Assert.False(string.IsNullOrWhiteSpace(iconsDirectory), "需要 MIJIA_ICONS_DIR 指向 mijia-product-icons 检出根");

        var workRoot = Path.Combine(Path.GetTempPath(), "seedlive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        try
        {
            var buildStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var build = await SeedPackBuilder.BuildAsync(
                new SeedPackBuildOptions
                {
                    IconsDirectory = iconsDirectory!,
                    OutputDirectory = workRoot,
                    SnapshotDate = "2026-09-28",
                    Source = "iop666/mijia-product-icons@2026-09-28",
                },
                new Progress<string>(message => output.WriteLine($"[build] {message}")));
            buildStopwatch.Stop();

            using var host = await DatabaseTestHost.CreateInitializedAsync();
            var importStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var importer = new SeedImporter(host.CreateContext(), host.Paths);
            var result = await importer.ImportAsync(
                build.PackagePath,
                new Progress<SeedImportProgress>(p => output.WriteLine(
                    $"[{p.Stage}] 产品 {p.ProductsDone}/{p.ProductsTotal}，图片 {p.ImagesDone}/{p.ImagesTotal}")),
                CancellationToken.None);
            importStopwatch.Stop();

            output.WriteLine(
                $"构建：产品 {build.ProductCount}（有图 {build.WithImage}，缺图 {build.MissingImage}，下架 {build.Delisted}，.old {build.OldImageCount}），" +
                $"包 {build.PackageBytes / 1024.0 / 1024.0:F1} MB，耗时 {buildStopwatch.Elapsed.TotalSeconds:F1}s");
            output.WriteLine(
                $"导入：新增 {result.ProductsAdded}，更新 {result.ProductsUpdated}，图片复制 {result.ImagesCopied}，" +
                $"跳过 {result.ImagesSkipped}，耗时 {importStopwatch.Elapsed.TotalSeconds:F1}s");

            Assert.Equal(10_547, result.ProductsTotal);
            Assert.Equal(10_547, result.ProductsAdded);
            Assert.Equal(10_544, result.ImageFilesTotal);

            await using var context = host.CreateContext();
            Assert.Equal(10_547, await context.Products.CountAsync());
            Assert.Equal(14, await context.Products.CountAsync(p => !p.IsAvailable));
            var auxRows = await context.Products.Where(p => p.Model.StartsWith("aux.")).ToListAsync();
            Assert.Equal(2, auxRows.Count);
            Assert.All(auxRows, row => Assert.Null(row.ImagePath));
            var noImage = await context.Products.SingleAsync(p => p.Model == "ows.heater.pdeh1a");
            Assert.Null(noImage.ImagePath);
            var heater = await context.Products.AsNoTracking().SingleAsync(p => p.Model == "zhimi.heater.za1");
            Assert.NotNull(heater.Sha256);
            Assert.True(File.Exists(Path.Combine(host.Paths.ImagesDirectory, "zhimi.heater.za1.png")));

            // 搜索性能：真实 10,547 产品上四个关键词，均应 <100ms。
            var search = new SearchService(host.CreateContext());
            foreach (var keyword in new[] { "空气", "小米", "camera", "xiaomi" })
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var found = await search.SearchAsync(keyword);
                stopwatch.Stop();
                output.WriteLine($"[search] '{keyword}' → {found.Count} 个产品，{stopwatch.Elapsed.TotalMilliseconds:F1}ms");
                Assert.NotEmpty(found);
                Assert.True(stopwatch.ElapsedMilliseconds < 100, $"搜索 {keyword} 耗时 {stopwatch.ElapsedMilliseconds}ms 超过 100ms");
            }

            // 筛选四验收场景：空气+环境电器、小米+有图片、已下架、无图片型号。
            var queryServiceInstance = new ProductQueryService(
                new TestDbContextFactory(() => host.CreateContext()),
                new FilterService(),
                new SortService());

            var airEnv = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Keyword = "空气",
                Filter = new ProductFilter { Categories = ["环境电器"] },
            });
            output.WriteLine($"[filter] 空气+环境电器 → {airEnv.Count}");
            Assert.NotEmpty(airEnv);
            Assert.All(airEnv, product => Assert.Equal("环境电器", product.Category));

            var xiaomiWithImage = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Keyword = "小米",
                Filter = new ProductFilter { Brands = ["小米出品"], HasImage = true },
            });
            output.WriteLine($"[filter] 小米出品+有图片 → {xiaomiWithImage.Count}");
            Assert.NotEmpty(xiaomiWithImage);
            Assert.All(xiaomiWithImage, product =>
            {
                Assert.Equal("小米出品", product.Brand);
                Assert.NotNull(product.Sha256);
            });

            var delisted = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Filter = new ProductFilter { IsAvailable = false },
            });
            output.WriteLine($"[filter] 已下架 → {delisted.Count}");
            Assert.Equal(14, delisted.Count);

            var noImageList = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Filter = new ProductFilter { HasImage = false },
            });
            output.WriteLine($"[filter] 无图片型号 → {noImageList.Count}");
            Assert.Equal(3, noImageList.Count);
            Assert.All(noImageList, product => Assert.Null(product.Sha256));

            // 排序三验收场景：空气+环境电器+使用次数降序、小米出品+有图片+更新时间降序、默认型号排序。
            var sortPipeline = new ProductQueryService(
                new TestDbContextFactory(() => host.CreateContext()),
                new FilterService(),
                new SortService());

            var airEnvByUsage = await sortPipeline.QueryAsync(new ProductQuery
            {
                Keyword = "空气",
                Filter = new ProductFilter { Categories = ["环境电器"] },
                Sort = new ProductSort { Field = ProductSortField.UsageCount, Direction = SortDirection.Descending },
            });
            output.WriteLine($"[sort] 空气+环境电器+使用次数降序 → {airEnvByUsage.Count}");
            Assert.Equal(airEnv.Count, airEnvByUsage.Count);
            Assert.All(airEnvByUsage, product => Assert.Equal("环境电器", product.Category));

            var xiaomiByUpdate = await sortPipeline.QueryAsync(new ProductQuery
            {
                Keyword = "小米",
                Filter = new ProductFilter { Brands = ["小米出品"], HasImage = true },
                Sort = new ProductSort { Field = ProductSortField.UpdateTime, Direction = SortDirection.Descending },
            });
            output.WriteLine($"[sort] 小米出品+有图片+更新时间降序 → {xiaomiByUpdate.Count}");
            Assert.Equal(xiaomiWithImage.Count, xiaomiByUpdate.Count);
            var updateTimes = xiaomiByUpdate.Select(product => product.UpdateTimeUnix ?? long.MinValue).ToList();
            Assert.Equal(updateTimes.OrderByDescending(value => value).ToList(), updateTimes);

            var byDefault = await sortPipeline.QueryAsync(new ProductQuery());
            var defaultModels = byDefault.Select(product => product.Model).ToList();
            Assert.Equal(defaultModels.OrderBy(model => model, StringComparer.Ordinal).ToList(), defaultModels);
            output.WriteLine($"[sort] 默认型号排序 → {defaultModels.Count}");

            // 随机浏览：真实 10,547 产品上三批 20 个，无重复且均满足筛选。
            var randomService = new ProductQueryService(
                new TestDbContextFactory(() => host.CreateContext()),
                new FilterService(),
                new SortService());
            var shown = new List<string>();
            long? cursor = null;
            for (var round = 0; round < 3; round++)
            {
                var batch = await randomService.QueryAsync(new ProductQuery
                {
                    Keyword = "小米",
                    Filter = new ProductFilter { Categories = ["环境电器"] },
                    Mode = BrowseMode.Random,
                    RandomCursor = cursor,
                    RandomLimit = 20,
                    ExcludeModels = shown,
                });
                output.WriteLine($"[random] 第 {round + 1} 批 → {batch.Count}（会话已展示 {shown.Count}）");
                Assert.All(batch, product => Assert.DoesNotContain(product.Model, shown));
                Assert.All(batch, product => Assert.Equal("环境电器", product.Category));
                shown.AddRange(batch.Select(product => product.Model));
                cursor = batch.Count == 0 ? cursor : batch.Max(product => product.RandomKey!.Value);
                if (batch.Count < 20)
                {
                    break;
                }
            }

            Assert.True(shown.Count > 0);

            // 收藏场景：把"空气+环境电器"命中的前 100 个加入收藏，再以收藏+分类+关键字组合查询。
            await using var favContext = host.CreateContext();
            var favoritesRepository = new FavoritesRepository(favContext);
            var airEnvAll = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Keyword = "空气",
                Filter = new ProductFilter { Categories = ["环境电器"] },
            });
            foreach (var product in airEnvAll.Take(100))
            {
                await favoritesRepository.AddAsync(product.Id, 1_800_000_000);
            }

            var favOnly = await queryServiceInstance.QueryAsync(new ProductQuery
            {
                Keyword = "空气",
                Filter = new ProductFilter
                {
                    Categories = ["环境电器"],
                    IsFavorite = true,
                },
                Sort = new ProductSort { Field = ProductSortField.Name, Direction = SortDirection.Ascending },
            });
            output.WriteLine($"[favorites] 收藏+空气+环境电器 → {favOnly.Count}");
            var expectedFavCount = Math.Min(100, airEnvAll.Count);
            Assert.Equal(expectedFavCount, favOnly.Count);
            var favoriteIdsInDb = (await favoritesRepository.GetFavoriteProductIdsAsync()).ToHashSet();
            Assert.All(favOnly, product => Assert.Contains(product.Id, favoriteIdsInDb));

            // Recent 场景：对前 100 个收藏产品记录 View/Copy/Drag 混合行为，验证数量/倒序/事件类型。
            var recentService = new RecentService(host.CreateContext());
            var usageRepoForRecent = new UsageRepository(favContext);
            for (var i = 0; i < Math.Min(100, favOnly.Count); i++)
            {
                var type = (UsageType)(i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2);
                await usageRepoForRecent.RecordAsync(favOnly[i].Id, type, 1_900_000_000 + i);
            }

            var recent = await recentService.GetRecentAsync(100);
            output.WriteLine($"[recent] 最近使用 → {recent.Count}");
            Assert.Equal(Math.Min(100, favOnly.Count), recent.Count);
            var times = recent.Select(row => row.LastUsedUnix).ToList();
            Assert.Equal(times.OrderByDescending(value => value).ToList(), times);
            Assert.All(recent, row => Assert.Contains(row.Product.Id, favoriteIdsInDb));
            Assert.Equal(UsageType.Drag, recent[0].LastEvent);
        }
        finally
        {
            try
            {
                Directory.Delete(workRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
