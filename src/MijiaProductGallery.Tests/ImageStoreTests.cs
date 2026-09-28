using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>原图存储测试：格式输入矩阵、生命周期、.old 链、aux 安全名与原图不可变。</summary>
public sealed class ImageStoreTests : IDisposable
{
    private readonly ImageTestHost _host = new();

    [Fact]
    public async Task StoreNew_Png_PlacesOriginalWithMetadata()
    {
        var store = _host.CreateStore();
        var bytes = ImageFixtures.CreatePng();

        var result = await store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(bytes));

        Assert.Equal(ImageStoreStatus.StoredNew, result.Status);
        Assert.Equal("zhimi.heater.za1.png", result.ImageFileName);
        Assert.Equal("images/zhimi.heater.za1.png", result.ImagePath);
        Assert.Equal(ImageFormat.Png, result.Format);
        Assert.Equal(64, result.Width);
        Assert.Equal(32, result.Height);
        Assert.Equal(bytes.Length, result.FileSize);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_host.Paths.ImagesDirectory, "zhimi.heater.za1.png")));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("webp")]
    public async Task StoreNew_AcceptsAllSupportedFormats(string format)
    {
        var store = _host.CreateStore();
        var bytes = format switch
        {
            "png" => ImageFixtures.CreatePng(),
            "jpg" => ImageFixtures.CreateJpeg(),
            "gif" => ImageFixtures.MinimalGif,
            _ => ImageFixtures.CreateWebp(),
        };
        var expectedExt = format == "jpg" ? ".jpg" : $".{format}";
        var expectedFormat = format switch
        {
            "png" => ImageFormat.Png,
            "jpg" => ImageFormat.Jpg,
            "gif" => ImageFormat.Gif,
            _ => ImageFormat.Webp,
        };

        var result = await store.StoreNewAsync("test.model.01", new MemoryStream(bytes));

        Assert.Equal(ImageStoreStatus.StoredNew, result.Status);
        Assert.Equal("test.model.01" + expectedExt, result.ImageFileName);
        Assert.Equal(expectedFormat, result.Format);
    }

    [Fact]
    public async Task StoreNew_CorrectsExtensionByContent()
    {
        var store = _host.CreateStore();
        var jpegBytes = ImageFixtures.CreateJpeg();

        var result = await store.StoreNewAsync("tuyun.camera.abc", new MemoryStream(jpegBytes));

        Assert.Equal("tuyun.camera.abc.jpg", result.ImageFileName);
        Assert.True(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "tuyun.camera.abc.jpg")));
        Assert.False(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "tuyun.camera.abc.png")));
    }

    [Fact]
    public async Task StoreNew_ZeroByte_Throws()
    {
        var store = _host.CreateStore();

        await Assert.ThrowsAsync<ImageValidationException>(
            () => store.StoreNewAsync("a.model.01", new MemoryStream([])));
    }

    [Fact]
    public async Task StoreNew_UnknownHeader_Throws()
    {
        var store = _host.CreateStore();

        await Assert.ThrowsAsync<ImageValidationException>(
            () => store.StoreNewAsync("a.model.01", new MemoryStream("plain text, not an image"u8.ToArray())));
    }

    [Fact]
    public async Task StoreNew_FakePng_Throws()
    {
        var store = _host.CreateStore();

        await Assert.ThrowsAsync<ImageValidationException>(
            () => store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.FakePng())));
    }

    [Fact]
    public async Task StoreNew_TruncatedPng_Throws()
    {
        var store = _host.CreateStore();

        await Assert.ThrowsAsync<ImageValidationException>(
            () => store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.TruncatedPng())));
    }

    [Fact]
    public async Task StoreNew_TwiceSameContent_ReturnsUnchanged()
    {
        var store = _host.CreateStore();
        var bytes = ImageFixtures.CreatePng();
        await store.StoreNewAsync("a.model.01", new MemoryStream(bytes));

        var second = await store.StoreNewAsync("a.model.01", new MemoryStream(bytes));

        Assert.Equal(ImageStoreStatus.Unchanged, second.Status);
    }

    [Fact]
    public async Task StoreNew_ExistingOrphanWithDifferentContent_Replaces()
    {
        var store = _host.CreateStore();
        await store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.CreatePng(64, 32)));

        var second = await store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.CreatePng(32, 64)));

        Assert.Equal(ImageStoreStatus.Replaced, second.Status);
    }

    [Fact]
    public async Task Replace_ShaChanged_OverwritesWithoutHistory()
    {
        var store = _host.CreateStore();
        await store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.CreatePng(64, 32)));

        var result = await store.ReplaceAsync("a.model.01", "a.model.01.png", new MemoryStream(ImageFixtures.CreatePng(32, 64)));

        Assert.Equal(ImageStoreStatus.Replaced, result.Status);
        Assert.Null(result.ReplacedByOldFileName);
        Assert.Empty(Directory.GetFiles(_host.Paths.ImagesDirectory, "a.model.01.old*"));
        Assert.True(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.png")));
    }

    [Fact]
    public async Task Replace_SameSha_ReturnsUnchanged()
    {
        var store = _host.CreateStore();
        var bytes = ImageFixtures.CreatePng();
        await store.StoreNewAsync("a.model.01", new MemoryStream(bytes));

        var result = await store.ReplaceAsync("a.model.01", "a.model.01.png", new MemoryStream(bytes));

        Assert.Equal(ImageStoreStatus.Unchanged, result.Status);
    }

    [Fact]
    public async Task Replace_ExtensionChanged_RemovesOldExtensionFile()
    {
        var store = _host.CreateStore();
        await store.StoreNewAsync("a.model.01", new MemoryStream(ImageFixtures.CreatePng()));

        var result = await store.ReplaceAsync("a.model.01", "a.model.01.png", new MemoryStream(ImageFixtures.CreateJpeg()));

        Assert.Equal(ImageStoreStatus.Replaced, result.Status);
        Assert.Equal("a.model.01.jpg", result.ImageFileName);
        Assert.True(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.jpg")));
        Assert.False(File.Exists(Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.png")));
    }

    [Fact]
    public async Task Replace_InvalidContent_OriginalStaysByteIdentical()
    {
        var store = _host.CreateStore();
        var original = ImageFixtures.CreatePng();
        await store.StoreNewAsync("a.model.01", new MemoryStream(original));

        await Assert.ThrowsAsync<ImageValidationException>(
            () => store.ReplaceAsync("a.model.01", "a.model.01.png", new MemoryStream(ImageFixtures.FakePng())));

        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_host.Paths.ImagesDirectory, "a.model.01.png")));
    }

    [Fact]
    public async Task ReplaceWithHistory_BuildsOldChainAcrossThreeGenerations()
    {
        var store = _host.CreateStore();
        var v1 = ImageFixtures.CreatePng(64, 32);
        var v2 = ImageFixtures.CreatePng(32, 64);
        var v3 = ImageFixtures.CreatePng(80, 16);
        var v4 = ImageFixtures.CreatePng(16, 80);
        await store.StoreNewAsync("chuangmi.camera.029a02", new MemoryStream(v1));

        var r2 = await store.ReplaceWithHistoryAsync("chuangmi.camera.029a02", "chuangmi.camera.029a02.png", new MemoryStream(v2));
        var r3 = await store.ReplaceWithHistoryAsync("chuangmi.camera.029a02", "chuangmi.camera.029a02.png", new MemoryStream(v3));
        var r4 = await store.ReplaceWithHistoryAsync("chuangmi.camera.029a02", "chuangmi.camera.029a02.png", new MemoryStream(v4));

        Assert.Equal("chuangmi.camera.029a02.old.png", r2.ReplacedByOldFileName);
        Assert.Equal("chuangmi.camera.029a02.old1.png", r3.ReplacedByOldFileName);
        Assert.Equal("chuangmi.camera.029a02.old2.png", r4.ReplacedByOldFileName);
        Assert.All(new[] { r2, r3, r4 }, result => Assert.Equal(ImageStoreStatus.ReplacedWithHistory, result.Status));

        var imagesDirectory = _host.Paths.ImagesDirectory;
        Assert.Equal(v1, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.old.png")));
        Assert.Equal(v2, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.old1.png")));
        Assert.Equal(v3, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.old2.png")));
        Assert.Equal(v4, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.png")));
    }

    [Fact]
    public async Task ReplaceWithHistory_SameSha_ReturnsUnchanged()
    {
        var store = _host.CreateStore();
        var bytes = ImageFixtures.CreatePng();
        await store.StoreNewAsync("a.model.01", new MemoryStream(bytes));

        var result = await store.ReplaceWithHistoryAsync("a.model.01", "a.model.01.png", new MemoryStream(bytes));

        Assert.Equal(ImageStoreStatus.Unchanged, result.Status);
    }

    [Fact]
    public async Task AuxModel_StoresWithSafeDiskNames_ButReportsRealChainNames()
    {
        var store = _host.CreateStore();
        var imagesDirectory = _host.Paths.ImagesDirectory;

        var stored = await store.StoreNewAsync("aux.aircondition.hc1", new MemoryStream(ImageFixtures.CreatePng()));

        Assert.Equal("aux.aircondition.hc1.png", stored.ImageFileName);
        Assert.Equal("images/aux_.aircondition.hc1.png", stored.ImagePath);
        Assert.True(File.Exists(Path.Combine(imagesDirectory, "aux_.aircondition.hc1.png")));

        var reused = await store.ReplaceWithHistoryAsync(
            "aux.aircondition.hc1",
            "aux.aircondition.hc1.png",
            new MemoryStream(ImageFixtures.CreatePng(32, 64)));

        Assert.Equal(ImageStoreStatus.ReplacedWithHistory, reused.Status);
        Assert.Equal("aux.aircondition.hc1.old.png", reused.ReplacedByOldFileName);
        Assert.True(File.Exists(Path.Combine(imagesDirectory, "aux_.aircondition.hc1.old.png")));
        Assert.True(File.Exists(Path.Combine(imagesDirectory, "aux_.aircondition.hc1.png")));
    }

    [Fact]
    public async Task ResolveAbsolutePath_ResolvesNormal_AndRejectsTraversal()
    {
        var store = _host.CreateStore();

        var resolved = store.ResolveAbsolutePath("images/a.model.01.png");

        Assert.NotNull(resolved);
        Assert.StartsWith(_host.Paths.Root, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("a.model.01.png", resolved, StringComparison.Ordinal);
        Assert.Null(store.ResolveAbsolutePath("images/../../escape.png"));
        Assert.Null(store.ResolveAbsolutePath(null));
        Assert.Null(store.ResolveAbsolutePath(string.Empty));
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
