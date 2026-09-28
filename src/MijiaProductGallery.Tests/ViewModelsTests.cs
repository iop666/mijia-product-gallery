using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.Tests;

/// <summary>图库视图模型测试：加载状态机、空库、错误路径。</summary>
public sealed class GalleryViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Load_WithProducts_TransitionsToReady()
    {
        var store = new ImageStore(host.Paths);
        var stored = await store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(ImageFixtures.CreatePng()));
        var products = new ProductRepository(host.CreateContext());
        await products.AddAsync(new Product
        {
            Model = "zhimi.heater.za1",
            Name = "智米电暖器智能版",
            Brand = "智米",
            Category = "环境电器",
            ImageFileName = stored.ImageFileName,
            ImagePath = stored.ImagePath,
            Sha256 = stored.Sha256,
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });
        await products.AddAsync(new Product
        {
            Model = "ows.heater.pdeh1a",
            Name = "无图型号",
            Brand = "b",
            Category = "环境电器",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });
        var vm = CreateViewModel(products);

        await vm.LoadAsync();

        Assert.Equal(GalleryLoadState.Ready, vm.State);
        Assert.Equal(2, vm.Cards.Count);
        Assert.True(vm.IsReadyVisible);
        // 默认按型号排序：ows 排在前（无图），zhimi 在后（有图）。
        Assert.False(vm.Cards[0].HasImage);
        Assert.True(vm.Cards[1].HasImage);
    }

    [Fact]
    public async Task Load_EmptyDatabase_EmptyState()
    {
        var vm = CreateViewModel(new ProductRepository(host.CreateContext()));

        await vm.LoadAsync();

        Assert.Equal(GalleryLoadState.Empty, vm.State);
        Assert.True(vm.IsEmptyVisible);
    }

    [Fact]
    public async Task Load_RepositoryThrows_ErrorState()
    {
        var vm = CreateViewModel(new ThrowingProductRepository());

        await vm.LoadAsync();

        Assert.Equal(GalleryLoadState.Error, vm.State);
        Assert.True(vm.IsErrorVisible);
        Assert.NotNull(vm.ErrorMessage);
    }

    private GalleryViewModel CreateViewModel(IProductRepository? repository = null)
    {
        return new GalleryViewModel(
            repository ?? new ProductRepository(host.CreateContext()),
            NewQueue(),
            CreateQueryService(),
            new SearchHistoryRepository(host.CreateContext()),
            new FavoritesRepository(host.CreateContext()),
            new InMemorySettings(),
            InlineUiDispatcher.Instance,
            debounceMilliseconds: 10);
    }

    private ProductQueryService CreateQueryService()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new ProductQueryService(factory, new FilterService(), new SortService());
    }

    private ThumbnailLoadQueue NewQueue()
    {
        return new ThumbnailLoadQueue(
            new ThumbnailService(host.Paths),
            InlineUiDispatcher.Instance,
            concurrency: 2);
    }

    private sealed class ThrowingProductRepository : IProductRepository
    {
        public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Product?> GetByModelAsync(string model, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsByModelAsync(string model, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("模拟数据库读取失败");
        }

        public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddAsync(Product product, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateOfficialFieldsAsync(Product product, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateSyncTimestampsAsync(IReadOnlyList<(string Model, long UpdateTimeUnix)> items, long lastSeenUnix, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>缩略图加载队列测试：按需加载成功与失败路径。</summary>
public sealed class ThumbnailLoadQueueTests : IAsyncLifetime
{
    private readonly ImageTestHost host = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Request_LoadsThumbnailForExistingImage()
    {
        var store = host.CreateStore();
        var stored = await store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(ImageFixtures.CreatePng()));
        var card = new ProductCard(new Product
        {
            Model = "zhimi.heater.za1",
            Name = "智米电暖器智能版",
            Brand = "智米",
            Category = "环境电器",
            ImageFileName = stored.ImageFileName,
            ImagePath = stored.ImagePath,
            Sha256 = stored.Sha256,
        });
        var queue = new ThumbnailLoadQueue(host.CreateThumbnailService(), InlineUiDispatcher.Instance, concurrency: 1);

        queue.Request(card);

        var completed = await WaitForAsync(() => card.ThumbnailState == CardThumbnailState.Loaded);
        Assert.True(completed);
        Assert.True(File.Exists(card.ThumbnailPath));
    }

    [Fact]
    public async Task Request_WithMissingOriginal_MarksFailed()
    {
        var card = new ProductCard(new Product
        {
            Model = "ghost.model.01",
            Name = "幽灵",
            Brand = "b",
            Category = "c",
            ImageFileName = "ghost.model.01.png",
            ImagePath = "images/ghost.model.01.png",
            Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        });
        var queue = new ThumbnailLoadQueue(host.CreateThumbnailService(), InlineUiDispatcher.Instance, concurrency: 1);

        queue.Request(card);

        var failed = await WaitForAsync(() => card.ThumbnailState == CardThumbnailState.Failed);
        Assert.True(failed);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 50)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}

/// <summary>初始化页视图模型测试：成功/失败路径与重试携带种子路径。</summary>
public sealed class InitializationViewModelTests
{
    [Fact]
    public async Task RunAsync_Success_RaisesSucceeded()
    {
        var initializer = new ScriptedInitializer(new InitializationReport
        {
            Outcome = InitializationOutcome.CompletedFromSeed,
            SnapshotDate = "2026-09-28",
            SeedResult = SeedResult("2026-09-28"),
        });
        var vm = new InitializationViewModel(initializer);
        var succeeded = false;
        vm.Succeeded += () => succeeded = true;

        await vm.RunAsync();

        Assert.True(succeeded);
        Assert.Equal(InitializationUiState.Succeeded, vm.State);
    }

    [Fact]
    public async Task RunAsync_Failure_ShowsMessageAndActions()
    {
        var initializer = new ScriptedInitializer(new InitializationReport
        {
            Outcome = InitializationOutcome.FailedNoSeedOffline,
            Message = "无法连接米家百科且未找到种子包",
            AvailableActions = ["retry", "check-seed", "online-init"],
        });
        var vm = new InitializationViewModel(initializer);

        await vm.RunAsync();

        Assert.Equal(InitializationUiState.Failed, vm.State);
        Assert.True(vm.IsFailed);
        Assert.True(vm.HasError);
        Assert.True(vm.CanPickSeed);
        Assert.Contains("check-seed", vm.AvailableActions);
    }

    [Fact]
    public async Task RetryWithSeed_PassesExplicitPath()
    {
        string? receivedPath = null;
        var initializer = new ScriptedInitializer(new InitializationReport
        {
            Outcome = InitializationOutcome.CompletedFromSeed,
            SnapshotDate = "2026-09-28",
        });
        initializer.OnInitialize = path => receivedPath = path;
        var vm = new InitializationViewModel(initializer);

        await vm.RetryWithSeedAsync("D:/seeds/seed-manual.zip");

        Assert.Equal("D:/seeds/seed-manual.zip", receivedPath);
        Assert.Equal(InitializationUiState.Succeeded, vm.State);
    }

    private static SeedImportResult SeedResult(string snapshotDate)
    {
        return new SeedImportResult
        {
            SnapshotDate = snapshotDate,
            ProductsTotal = 3,
            ProductsAdded = 3,
            ProductsUpdated = 0,
            ImageFilesTotal = 2,
            ImagesSkipped = 0,
            ImagesCopied = 2,
            ImagesRepaired = 0,
        };
    }

    private sealed class ScriptedInitializer(InitializationReport report) : IFirstRunInitializer
    {
        public Action<string?>? OnInitialize { get; set; }

        public Task<InitializationReport> InitializeAsync(
            string? explicitSeedPath = null,
            IProgress<SeedImportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            OnInitialize?.Invoke(explicitSeedPath);
            return Task.FromResult(report);
        }
    }
}
