using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
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
