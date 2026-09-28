using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Http;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>米家百科接口桩：脚本化分类与各分类产品。</summary>
public sealed class FakeBaikeApiClient : IBaikeApiClient
{
    public List<BaikeCategory> Categories { get; } = [];

    public Dictionary<int, List<BaikeProductDto>> ProductsByCategory { get; } = [];

    public int GetProductsCalls { get; private set; }

    /// <summary>注入后 GetCategoriesAsync 抛出该异常（模拟网络不可达）。</summary>
    public Exception? CategoriesError { get; set; }

    public Task<IReadOnlyList<BaikeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (CategoriesError is not null)
        {
            throw CategoriesError;
        }

        return Task.FromResult<IReadOnlyList<BaikeCategory>>([.. Categories]);
    }

    public Task<IReadOnlyList<BaikeProductDto>> GetProductsByCategoryAsync(int ptId, CancellationToken cancellationToken = default)
    {
        GetProductsCalls++;
        return Task.FromResult<IReadOnlyList<BaikeProductDto>>(
            ProductsByCategory.GetValueOrDefault(ptId) ?? []);
    }

    public void AddCategory(int ptId, string name)
    {
        Categories.Add(new BaikeCategory { PtId = ptId, Name = name });
    }

    public void AddProduct(int ptId, string model, string name, string brand, long createTime, long updateTime, string? realIcon = null)
    {
        if (!ProductsByCategory.TryGetValue(ptId, out var list))
        {
            list = [];
            ProductsByCategory[ptId] = list;
        }

        list.Add(new BaikeProductDto
        {
            Model = model,
            Name = name,
            Brand = brand,
            RealIcon = realIcon ?? $"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/{model}.png",
            CreateTimeUnix = createTime,
            UpdateTimeUnix = updateTime,
            PtId = ptId,
        });
    }
}

/// <summary>图片下载桩：按 URL 返回脚本内容，可配置失败。</summary>
public sealed class FakeImageDownloader : IImageDownloader
{
    public Dictionary<string, byte[]> Responses { get; } = [];

    public HashSet<string> FailingUrls { get; } = [];

    public int CallCount { get; private set; }

    public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (FailingUrls.Contains(url) || !Responses.TryGetValue(url, out var bytes))
        {
            throw new ImageDownloadException($"桩配置为失败或无响应：{url}");
        }

        return Task.FromResult(bytes);
    }
}

/// <summary>同步引擎测试的组装宿主：真实数据库 + 真实图片系统 + 桩接口。</summary>
public sealed class SyncTestHost : IDisposable
{
    public DatabaseTestHost Database { get; } = DatabaseTestHost.CreateNotInitialized();

    public FakeBaikeApiClient Api { get; } = new();

    public FakeImageDownloader Downloader { get; } = new();

    private bool initialized;

    /// <summary>组装同步引擎（自动确保数据库已初始化）。</summary>
    public async Task<SyncEngine> CreateEngineAsync()
    {
        await EnsureInitializedAsync();
        return new SyncEngine(
            Api,
            Downloader,
            new ImageStore(Database.Paths),
            new ThumbnailService(Database.Paths),
            new ProductRepository(Database.CreateContext()),
            new SyncStateRepository(Database.CreateContext()));
    }

    /// <summary>首次调用时初始化数据库（幂等）。</summary>
    public async Task EnsureInitializedAsync()
    {
        if (initialized)
        {
            return;
        }

        await using var context = Database.CreateContext();
        await new DbInitializer(context, Database.Paths).InitializeAsync();
        initialized = true;
    }

    public GalleryDbContext CreateContext()
    {
        return Database.CreateContext();
    }

    /// <summary>直接经图片系统与仓储预置一个带图产品（模拟上一轮同步的产物）。</summary>
    public async Task<Product> SeedProductWithImageAsync(
        string model,
        string name,
        string category,
        byte[] imageBytes,
        long createTime = 1_500_000_000,
        long updateTime = 1_600_000_000)
    {
        await EnsureInitializedAsync();
        var store = new ImageStore(Database.Paths);
        var stored = await store.StoreNewAsync(model, new MemoryStream(imageBytes));
        var context = Database.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product
        {
            Model = model,
            Name = name,
            Brand = "小米出品",
            Category = category,
            ImageFileName = stored.ImageFileName,
            ImagePath = stored.ImagePath,
            ImageUrl = $"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/{model}.png",
            ImageFormat = stored.Format,
            ImageWidth = stored.Width,
            ImageHeight = stored.Height,
            FileSize = stored.FileSize,
            Sha256 = stored.Sha256,
            IsAvailable = true,
            CreateTimeUnix = createTime,
            UpdateTimeUnix = updateTime,
            FirstSeenUnix = 1_600_000_000,
            LastSeenUnix = 1_600_000_000,
        };
        await products.AddAsync(product);
        return product;
    }

    public void Dispose()
    {
        Database.Dispose();
    }
}
