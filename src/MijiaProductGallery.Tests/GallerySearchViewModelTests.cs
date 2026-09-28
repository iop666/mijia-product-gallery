using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>图库搜索视图模型测试：防抖、世代丢弃、结果摘要、历史记录、建议。</summary>
public sealed class GallerySearchViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public GallerySearchViewModelTests(Xunit.Abstractions.ITestOutputHelper testOutputHelper)
    {
        output = testOutputHelper;
    }

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        var store = new ImageStore(host.Paths);
        var stored = await store.StoreNewAsync("xiaomi.airp.mp5b", new MemoryStream(ImageFixtures.CreatePng()));
        await new ProductRepository(host.CreateContext()).AddRangeAsync(
        [
            new Product
            {
                Model = "xiaomi.airp.mp5b",
                Name = "米家空气净化器 5 小米出品",
                Brand = "小米出品",
                Category = "个护与起居",
                ImageFileName = stored.ImageFileName,
                ImagePath = stored.ImagePath,
                Sha256 = stored.Sha256,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "chuangmi.camera.029a02",
                Name = "小米智能摄像机",
                Brand = "小米出品",
                Category = "安防",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
        ]);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private GalleryViewModel CreateViewModel(IProductRepository? repository = null, int debounceMilliseconds = 10)
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            repository ?? new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new SearchHistoryRepository(host.CreateContext()),
            new InMemorySettings(),
            InlineUiDispatcher.Instance,
            debounceMilliseconds);
    }

    private ThumbnailService host2Service()
    {
        return new ThumbnailService(host.Paths);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        return await WaitForAsync(() => Task.FromResult(condition()), timeoutMilliseconds);
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 40)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(40);
        }

        return await condition();
    }

    [Fact]
    public async Task DebouncedSearch_RunsOnce_AndRecordsHistory()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        var historyRepository = new SearchHistoryRepository(host.CreateContext());

        vm.SearchText = "空气";
        await Task.Delay(200);

        Assert.True(await WaitForAsync(() => vm.Cards.Count == 1));
        Assert.Equal(GalleryLoadState.Ready, vm.State);
        Assert.Equal("空气 · 找到 1 个产品", vm.ResultSummary);
        var entry = Assert.Single(await historyRepository.GetRecentAsync(10));
        Assert.Equal("空气", entry.Query);
        Assert.Equal(1, entry.ResultCount);
    }

    [Fact]
    public async Task RapidInput_OnlyLatestGenerationApplies()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.SearchText = "米";
        vm.SearchText = "zzz-no-match";

        Assert.True(await WaitForAsync(() => vm.ResultSummary == "没有找到相关产品"));
        Assert.Empty(vm.Cards);
        var historyRepository = new SearchHistoryRepository(host.CreateContext());
        Assert.True(await WaitForAsync(
            async () => (await historyRepository.GetRecentAsync(10)).Count == 1));
    }

    [Fact]
    public async Task EmptyKeyword_ReturnsToFullGallery()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.SearchText = "空气";
        await WaitForAsync(() => vm.Cards.Count == 1);

        vm.SearchText = " ";
        Assert.True(await WaitForAsync(() => vm.Cards.Count == 2 && vm.ResultSummary == "共 2 个产品"));
    }

    [Fact]
    public async Task NoMatch_ShowsNotFoundSummary()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.ApplySearchImmediate("zzz-no-match");

        Assert.True(await WaitForAsync(() => vm.ResultSummary == "没有找到相关产品"));
        Assert.Equal(GalleryLoadState.Empty, vm.State);
        Assert.True(vm.IsEmptyVisible);
    }

    [Fact]
    public async Task Suggestions_FilterByPrefix()
    {
        var historyRepository = new SearchHistoryRepository(host.CreateContext());
        await historyRepository.AddAsync("空气净化器", 1_700_000_100, 5);
        await historyRepository.AddAsync("摄像头", 1_700_000_200, 6);
        await historyRepository.AddAsync("小米电视", 1_700_000_300, 7);
        var vm = CreateViewModel();

        var all = await vm.GetSearchSuggestionsAsync(null);
        var filtered = await vm.GetSearchSuggestionsAsync("空气");

        Assert.Equal(3, all.Count);
        Assert.Equal(["空气净化器"], filtered);
    }

    [Fact]
    public async Task SearchFailure_ShowsErrorState()
    {
        var vm = CreateViewModel(new ThrowingProductRepository());
        await vm.LoadAsync();
        Assert.Equal(GalleryLoadState.Error, vm.State);
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
