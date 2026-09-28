using SkiaSharp;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>缩略图缓存测试：sha8 命名、命中缓存、换名、损坏自愈、原图只读。</summary>
public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly ImageTestHost _host = new();

    [Fact]
    public async Task Ensure_CreatesWebpNamedBySha8()
    {
        var service = _host.CreateThumbnailService();
        var sha = await StoreOriginalAsync("zhimi.heater.za1", "png");

        var path = await service.EnsureThumbnailAsync("zhimi.heater.za1.png", sha);

        Assert.True(File.Exists(path));
        Assert.Contains("zhimi.heater.za1." + sha[..8] + ".webp", path, StringComparison.Ordinal);
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
        Assert.True(decoded.Width > 0);
    }

    [Fact]
    public async Task Ensure_Twice_HitsCacheWithoutRewrite()
    {
        var service = _host.CreateThumbnailService();
        var sha = await StoreOriginalAsync("a.model.01", "png");

        var first = await service.EnsureThumbnailAsync("a.model.01.png", sha);
        var beforeWrite = File.GetLastWriteTimeUtc(first);
        var second = await service.EnsureThumbnailAsync("a.model.01.png", sha);

        Assert.Equal(first, second);
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(first));
    }

    [Fact]
    public async Task ShaChange_PointsToNewThumbnail_OldOneDeletable()
    {
        var service = _host.CreateThumbnailService();
        var shaA = await StoreOriginalAsync("a.model.01", "png");
        var pathA = await service.EnsureThumbnailAsync("a.model.01.png", shaA);

        var shaB = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var pathB = await service.EnsureThumbnailAsync("a.model.01.png", shaB);

        Assert.NotEqual(pathA, pathB);
        await service.DeleteThumbnailAsync("a.model.01.png", shaA);
        Assert.False(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
    }

    [Fact]
    public async Task CorruptedThumbnail_SelfHealsOnNextEnsure()
    {
        var service = _host.CreateThumbnailService();
        var sha = await StoreOriginalAsync("a.model.01", "png");
        var path = await service.EnsureThumbnailAsync("a.model.01.png", sha);
        await File.WriteAllBytesAsync(path, "not a webp at all"u8.ToArray());

        var healed = await service.EnsureThumbnailAsync("a.model.01.png", sha);

        Assert.Equal(path, healed);
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
    }

    [Fact]
    public async Task Ensure_WithMissingOriginal_Throws()
    {
        var service = _host.CreateThumbnailService();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.EnsureThumbnailAsync("ghost.model.01.png", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }

    [Fact]
    public async Task AllOperations_LeaveOriginalByteIdentical()
    {
        var service = _host.CreateThumbnailService();
        var originalPath = Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.png");
        var sha = await StoreOriginalAsync("a.model.01", "png");
        var before = await File.ReadAllBytesAsync(originalPath);

        await service.EnsureThumbnailAsync("a.model.01.png", sha);
        await service.EnsureThumbnailAsync("a.model.01.png", sha);
        await service.DeleteThumbnailAsync("a.model.01.png", sha);
        await service.EnsureThumbnailAsync("a.model.01.png", sha);

        Assert.Equal(before, await File.ReadAllBytesAsync(originalPath));
    }

    [Fact]
    public async Task DeleteAll_RemovesEveryThumbnail_ButKeepsImages()
    {
        var service = _host.CreateThumbnailService();
        var sha = await StoreOriginalAsync("a.model.01", "png");
        await service.EnsureThumbnailAsync("a.model.01.png", sha);

        await service.DeleteAllThumbnailsAsync();

        Assert.Empty(Directory.GetFiles(_host.Paths.ThumbnailsDirectory, "*", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.png")));
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task<string> StoreOriginalAsync(string model, string ext)
    {
        var bytes = ext switch
        {
            "jpg" => ImageFixtures.CreateJpeg(),
            _ => ImageFixtures.CreatePng(),
        };
        var store = _host.CreateStore();
        var result = await store.StoreNewAsync(model, new MemoryStream(bytes));
        return result.Sha256;
    }
}
