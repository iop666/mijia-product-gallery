using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 收藏夹 ZIP 导出服务：流式写入、Model / 设备名称两种命名、非法文件名安全化、
/// 重名自动 " (n)" 去重、缺图计入失败（绝不生成空文件）、取消删除半成品。
/// 服务为纯文件操作，不接触数据库（业务边界：导出只读）。
/// </summary>
public sealed class CollectionExportServiceTests : IDisposable
{
    private readonly string root;
    private readonly CollectionExportService service = new();

    public CollectionExportServiceTests()
    {
        root = Path.Combine(Path.GetTempPath(), "mpg-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private string ImageFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, content ?? [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    private static CollectionExportItem Item(int id, string model, string name, string? imagePath)
    {
        return new CollectionExportItem { ProductId = id, Model = model, Name = name, ImagePath = imagePath };
    }

    private async Task<List<string>> EntryNamesAsync(string zipPath)
    {
        await using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    [Fact]
    public async Task Export_ByModel_UsesModelAndOriginalExtension()
    {
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "xiaomi.airp.mp5b", "小米无线耳机", ImageFile("a.png")),
            ],
            ZipFilePath = zip,
            NameByModel = true,
        });

        Assert.False(result.Cancelled);
        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(["xiaomi.airp.mp5b.png"], await EntryNamesAsync(zip));
    }

    [Fact]
    public async Task Export_ByName_UsesDeviceName()
    {
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "chumi.cooker.v1", "小米空气净化器", ImageFile("a.png")),
            ],
            ZipFilePath = zip,
            NameByModel = false,
        });

        Assert.Equal(["小米空气净化器.png"], await EntryNamesAsync(zip));
    }

    [Fact]
    public async Task Export_DuplicateDeviceNames_GetSerialSuffix_NoOverwrite()
    {
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "m.1", "小米空气净化器", ImageFile("a.png")),
                Item(2, "m.2", "小米空气净化器", ImageFile("b.png")),
                Item(3, "m.3", "小米空气净化器", ImageFile("c.png")),
            ],
            ZipFilePath = zip,
            NameByModel = false,
        });

        Assert.Equal(3, result.ExportedCount);
        Assert.Equal(
            ["小米空气净化器.png", "小米空气净化器 (1).png", "小米空气净化器 (2).png"],
            await EntryNamesAsync(zip));
    }

    [Fact]
    public async Task Export_InvalidFileNameChars_AreSanitized()
    {
        var zip = Path.Combine(root, "out.zip");
        await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "m.1", "产品<>:\"/\\|?*A", ImageFile("a.png")),
            ],
            ZipFilePath = zip,
            NameByModel = false,
        });

        var names = await EntryNamesAsync(zip);
        var entry = Assert.Single(names);
        Assert.DoesNotContain("<", entry);
        Assert.DoesNotContain(">", entry);
        Assert.DoesNotContain(":", entry);
        Assert.DoesNotContain("?", entry);
        Assert.EndsWith(".png", entry);
    }

    [Fact]
    public async Task Export_EmptyName_FallsBackToModel()
    {
        var zip = Path.Combine(root, "out.zip");
        await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "chumi.model.v1", "   ", ImageFile("a.png")),
            ],
            ZipFilePath = zip,
            NameByModel = false,
        });

        Assert.Equal(["chumi.model.v1.png"], await EntryNamesAsync(zip));
    }

    [Fact]
    public async Task Export_MissingImage_CountsFailed_NoEmptyEntries()
    {
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "m.ok", "有图产品", ImageFile("a.png")),
                Item(2, "m.miss", "无图产品", null),
                Item(3, "m.gone", "文件丢失", Path.Combine(root, "not-exist.png")),
            ],
            ZipFilePath = zip,
            NameByModel = true,
        });

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(2, result.FailedItems.Count);
        var names = await EntryNamesAsync(zip);
        Assert.Single(names);
        Assert.DoesNotContain("m.miss", names[0]);
    }

    [Fact]
    public async Task Export_Cancel_DeletesPartialZip()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(
            new CollectionExportRequest
            {
                Items =
                [
                    Item(1, "m.1", "产品1", ImageFile("a.png")),
                    Item(2, "m.2", "产品2", ImageFile("b.png")),
                ],
                ZipFilePath = zip,
                NameByModel = true,
            },
            progress: null,
            cancellationToken: cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Export_ProgressReportsEachItem()
    {
        var reports = new List<CollectionExportProgress>();
        var progress = new Progress<CollectionExportProgress>(reports.Add);
        var zip = Path.Combine(root, "out.zip");
        await service.ExportAsync(new CollectionExportRequest
        {
            Items =
            [
                Item(1, "m.1", "产品1", ImageFile("a.png")),
                Item(2, "m.2", "产品2", ImageFile("b.png")),
            ],
            ZipFilePath = zip,
            NameByModel = true,
        }, progress);

        // Progress<T> 异步投递，等待回调完成。
        await Task.Delay(100);
        Assert.Equal(2, reports.Count);
        Assert.Equal((1, 2), (reports[0].Completed, reports[0].Total));
        Assert.Equal((2, 2), (reports[1].Completed, reports[1].Total));
    }

    [Fact]
    public async Task Export_StreamsZip_LargeImageContent()
    {
        // 大内容（>2MB）流式写入：条目内容完整可读。
        var big = new byte[2 * 1024 * 1024 + 123];
        new Random(7).NextBytes(big);
        var zip = Path.Combine(root, "out.zip");
        var result = await service.ExportAsync(new CollectionExportRequest
        {
            Items = [Item(1, "m.big", "大图", ImageFile("big.png", big))],
            ZipFilePath = zip,
            NameByModel = true,
        });

        Assert.Equal(1, result.ExportedCount);
        await using var stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        await using var entryStream = entry.Open();
        using var ms = new MemoryStream();
        await entryStream.CopyToAsync(ms);
        Assert.Equal(big.Length, ms.Length);
    }

    [Fact]
    public void ExportService_IsReadOnlyBoundary_DoesNotTouchRepositories()
    {
        // 业务边界：导出服务不依赖任何仓储/数据库类型（只读数据的结构性保证）。
        var dependencies = typeof(CollectionExportService).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();
        Assert.Empty(dependencies);
        Assert.DoesNotContain(typeof(IProductRepository), dependencies);
    }
}
