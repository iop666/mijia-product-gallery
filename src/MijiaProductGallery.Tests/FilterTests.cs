using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>筛选服务与组合查询测试：维度、组合、边界（真实 SQLite）。</summary>
public sealed class ProductFilterTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public ProductFilterTests(Xunit.Abstractions.ITestOutputHelper testOutputHelper)
    {
        output = testOutputHelper;
    }

    private const long NowUnix = 1_800_000_000;

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();

        var store = new ImageStore(host.Paths);
        var image = await store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(ImageFixtures.CreatePng()));
        var products = new ProductRepository(context);
        await products.AddRangeAsync(
        [
            new Product
            {
                Model = "zhimi.heater.za1",
                Name = "智米电暖器智能版",
                Brand = "智米",
                Category = "环境电器",
                ImageFileName = image.ImageFileName,
                ImagePath = image.ImagePath,
                Sha256 = image.Sha256,
                IsAvailable = true,
                UpdateTimeUnix = NowUnix,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "xiaomi.airp.mp5b",
                Name = "米家空气净化器 5 小米出品",
                Brand = "小米出品",
                Category = "个护与起居",
                Sha256 = "b".PadRight(64, 'b'),
                IsAvailable = true,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "chuangmi.camera.029a02",
                Name = "小米智能摄像机",
                Brand = "小米出品",
                Category = "安防",
                Sha256 = "c".PadRight(64, 'c'),
                IsAvailable = false,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "ows.heater.pdeh1a",
                Name = "无图电暖器",
                Brand = "智米",
                Category = "环境电器",
                IsAvailable = true,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "midjd.fridge.bs42s",
                Name = "已下架冰箱",
                Brand = "小米出品",
                Category = "厨房电器",
                IsAvailable = false,
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
        ]);

        // 使用计数：A=2（已使用），B=12（高频），C/D/E 无记录（从未使用）。
        var usageRepository = new UsageRepository(context);
        await usageRepository.RecordAsync(
            (await products.GetByModelAsync("zhimi.heater.za1"))!.Id, UsageType.Copy, 1_700_000_000);
        await usageRepository.RecordAsync(
            (await products.GetByModelAsync("zhimi.heater.za1"))!.Id, UsageType.Copy, 1_700_000_100);
        await usageRepository.RecordAsync(
            (await products.GetByModelAsync("xiaomi.airp.mp5b"))!.Id, UsageType.Drag, 1_700_000_200);
        for (var i = 0; i < 10; i++)
        {
            await usageRepository.RecordAsync(
                (await products.GetByModelAsync("xiaomi.airp.mp5b"))!.Id, UsageType.View, 1_700_000_300 + i);
        }
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private (IFilterService Filter, IProductQueryService Query) CreatePipeline()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        var filter = new FilterService(new FixedTimeProvider(NowUnix));
        return (filter, new ProductQueryService(factory, filter, new SortService()));
    }

    private async Task<IReadOnlyList<Product>> ApplyFilterAsync(ProductFilter filter, string? keyword = null)
    {
        var (_, query) = CreatePipeline();
        return await query.QueryAsync(new ProductQuery { Keyword = keyword, Filter = filter });
    }

    [Fact]
    public async Task Category_Single_MatchesOnlyThatCategory()
    {
        var results = await ApplyFilterAsync(new ProductFilter { Categories = ["环境电器"] });
        Assert.All(results, product => Assert.Equal("环境电器", product.Category));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Category_Multiple_IsOrWithinDimension()
    {
        var results = await ApplyFilterAsync(new ProductFilter { Categories = ["安防", "厨房电器"] });
        Assert.Equal(2, results.Count);
        Assert.All(results, product => Assert.Contains(product.Category, new[] { "安防", "厨房电器" }));
    }

    [Fact]
    public async Task Brand_Single_Matches()
    {
        var results = await ApplyFilterAsync(new ProductFilter { Brands = ["智米"] });
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Category_And_Brand_IsIntersection()
    {
        var results = await ApplyFilterAsync(new ProductFilter
        {
            Categories = ["环境电器"],
            Brands = ["小米出品"],
        });

        Assert.Empty(results);
    }

    [Fact]
    public async Task Availability_BothDirections()
    {
        var available = await ApplyFilterAsync(new ProductFilter { IsAvailable = true });
        Assert.Equal(3, available.Count);

        var delisted = await ApplyFilterAsync(new ProductFilter { IsAvailable = false });
        Assert.Equal(2, delisted.Count);
        Assert.All(delisted, product => Assert.False(product.IsAvailable));
    }

    [Fact]
    public async Task HasImage_BothDirections()
    {
        var withImage = await ApplyFilterAsync(new ProductFilter { HasImage = true });
        Assert.Equal(3, withImage.Count);

        var withoutImage = await ApplyFilterAsync(new ProductFilter { HasImage = false });
        Assert.Equal(2, withoutImage.Count);
        Assert.All(withoutImage, product => Assert.Null(product.Sha256));
    }

    [Fact]
    public async Task Usage_NeverUsed_Used_HighUsage()
    {
        var never = await ApplyFilterAsync(new ProductFilter { Usage = UsageRange.NeverUsed });
        var used = await ApplyFilterAsync(new ProductFilter { Usage = UsageRange.Used });
        var high = await ApplyFilterAsync(new ProductFilter { Usage = UsageRange.HighUsage });

        Assert.Equal(3, never.Count);
        Assert.Equal(2, used.Count);
        Assert.Equal("xiaomi.airp.mp5b", Assert.Single(high).Model);
    }

    [Fact]
    public async Task UpdateTime_Last30Days_MatchesRecentOnly()
    {
        var results = await ApplyFilterAsync(new ProductFilter { UpdateTime = DateRange.Last30Days });
        Assert.Equal("zhimi.heater.za1", Assert.Single(results).Model);
    }

    [Fact]
    public async Task EmptyFilter_ReturnsAll()
    {
        var results = await ApplyFilterAsync(new ProductFilter());
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task NonExistentCategory_ReturnsEmpty()
    {
        var results = await ApplyFilterAsync(new ProductFilter { Categories = ["不存在的分类"] });
        Assert.Empty(results);
    }

    [Fact]
    public async Task DuplicateValues_AreHarmless()
    {
        var results = await ApplyFilterAsync(new ProductFilter { Categories = ["环境电器", "环境电器"] });
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Keyword_Plus_Category_IsIntersection()
    {
        // 名称含"空气"的有 1 个（个护与起居）；加大类筛选"环境电器"后为空集。
        var results = await ApplyFilterAsync(
            new ProductFilter { Categories = ["环境电器"] },
            keyword: "空气");
        Assert.Empty(results);

        // 名称含"电暖器"且大类为环境电器：交集非空。
        var matched = await ApplyFilterAsync(
            new ProductFilter { Categories = ["环境电器"] },
            keyword: "电暖器");
        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public async Task Keyword_Plus_Brand_IsIntersection()
    {
        var results = await ApplyFilterAsync(
            new ProductFilter { Brands = ["小米出品"] },
            keyword: "小米");
        Assert.All(results, product => Assert.Equal("小米出品", product.Brand));
        // 命中"小米"的行里品牌为小米出品的有：B(名称)、C(名称)、E(品牌本身)。
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task AllDimensions_Combined()
    {
        var results = await ApplyFilterAsync(new ProductFilter
        {
            Categories = ["环境电器"],
            Brands = ["智米"],
            IsAvailable = true,
            HasImage = true,
            Usage = UsageRange.Used,
            UpdateTime = DateRange.Last30Days,
        });

        Assert.Equal("zhimi.heater.za1", Assert.Single(results).Model);
    }

    [Fact]
    public async Task EmptyKeyword_Query_ReturnsAllOrderedByModel()
    {
        var (_, query) = CreatePipeline();
        var results = await query.QueryAsync(new ProductQuery());
        Assert.Equal(5, results.Count);
        Assert.Equal(results.OrderBy(product => product.Model, StringComparer.Ordinal).ToList(), results);
    }

    private sealed class FixedTimeProvider(long unixSeconds) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
    }
}

/// <summary>筛选面板视图模型测试：构建/恢复/重置/持久化。</summary>
public sealed class FilterPaneViewModelTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        await new ProductRepository(host.CreateContext()).AddRangeAsync(
        [
            new Product { Model = "a.model.01", Name = "甲", Brand = "品牌一", Category = "分类一", FirstSeenUnix = 1, LastSeenUnix = 1 },
            new Product { Model = "b.model.02", Name = "乙", Brand = "品牌二", Category = "分类二", FirstSeenUnix = 1, LastSeenUnix = 1 },
        ]);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private async Task<FilterPaneViewModel> CreateLoadedPaneAsync()
    {
        var pane = new FilterPaneViewModel(settings);
        await pane.LoadAsync(new ProductRepository(host.CreateContext()), settings);
        return pane;
    }

    [Fact]
    public async Task BuildFilter_NoSelection_ReturnsNull()
    {
        var pane = await CreateLoadedPaneAsync();
        Assert.Null(pane.BuildFilter());
        Assert.True(pane.IsEmpty);
    }

    [Fact]
    public async Task SelectCategory_BuildFilterContainsIt()
    {
        var pane = await CreateLoadedPaneAsync();
        pane.SetCategorySelected("分类一", true);

        var filter = pane.BuildFilter();
        Assert.NotNull(filter);
        Assert.Equal(["分类一"], filter.Categories);
        Assert.False(pane.IsEmpty);
    }

    [Fact]
    public async Task Reset_ClearsAllSelections()
    {
        var pane = await CreateLoadedPaneAsync();
        pane.SetCategorySelected("分类一", true);
        pane.Availability = AvailabilityOption.DelistedOnly;

        pane.Reset();

        Assert.Null(pane.BuildFilter());
        Assert.True(pane.IsEmpty);
    }

    [Fact]
    public async Task Selection_PersistsToSettings_AndRestores()
    {
        var pane = await CreateLoadedPaneAsync();
        pane.SetCategorySelected("分类二", true);
        pane.ImageOptionValue = ImageOption.WithImage;

        // 新面板从同一设置仓储恢复。
        var restored = new FilterPaneViewModel(settings);
        await restored.LoadAsync(new ProductRepository(host.CreateContext()), settings);

        var filter = restored.BuildFilter();
        Assert.NotNull(filter);
        Assert.Equal(["分类二"], filter.Categories);
        Assert.True(filter.HasImage);
    }

    [Fact]
    public async Task CategoryChange_RaisesFilterChanged()
    {
        var pane = await CreateLoadedPaneAsync();
        var raised = 0;
        pane.FilterChanged += () => raised++;

        pane.SetCategorySelected("分类一", true);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task LoadOptions_FillsDistinctSortedValues()
    {
        var pane = await CreateLoadedPaneAsync();
        Assert.Equal(["分类一", "分类二"], pane.Categories.Select(option => option.Label).ToList());
        Assert.Equal(["品牌一", "品牌二"], pane.Brands.Select(option => option.Label).ToList());
    }
}

/// <summary>图库筛选 Chip 集成测试：生成、单个移除、清除全部。</summary>
public sealed class GalleryFilterChipTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        await new ProductRepository(host.CreateContext()).AddRangeAsync(
        [
            new Product { Model = "a.model.01", Name = "甲", Brand = "品牌一", Category = "分类一", FirstSeenUnix = 1, LastSeenUnix = 1 },
            new Product { Model = "b.model.02", Name = "乙", Brand = "品牌二", Category = "分类二", FirstSeenUnix = 1, LastSeenUnix = 1 },
        ]);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new SearchHistoryRepository(host.CreateContext()),
            new FavoritesRepository(host.CreateContext()),
            new InMemorySettings(),
            InlineUiDispatcher.Instance,
            debounceMilliseconds: 10);
    }

    [Fact]
    public async Task SelectCategory_ProducesChip_AndFiltersCards()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.FilterPane.SetCategorySelected("分类一", true);
        await WaitForAsync(() => vm.State == GalleryLoadState.Ready && vm.Cards.Count == 1);

        Assert.Contains(vm.ActiveChips, chip => chip.Id == "cat:分类一");
        Assert.Equal("分类一", vm.Cards.Single().Category);
    }

    [Fact]
    public async Task RemoveCategoryChip_RestoresUnfilteredCards()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.FilterPane.SetCategorySelected("分类一", true);
        await WaitForAsync(() => vm.Cards.Count == 1);

        vm.RemoveChip("cat:分类一");
        await WaitForAsync(() => vm.Cards.Count == 2);

        Assert.DoesNotContain(vm.ActiveChips, chip => chip.Id == "cat:分类一");
    }

    [Fact]
    public async Task KeywordChip_Removed_ClearsSearch()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.ApplySearchImmediate("甲");
        await WaitForAsync(() => vm.Cards.Count == 1);

        Assert.Contains(vm.ActiveChips, chip => chip.Id == "search");
        vm.RemoveChip("search");
        await WaitForAsync(() => vm.Cards.Count == 2);

        Assert.DoesNotContain(vm.ActiveChips, chip => chip.Id == "search");
    }

    [Fact]
    public async Task ClearAllFilters_RemovesEverything()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        vm.ApplySearchImmediate("甲");
        await WaitForAsync(() => vm.Cards.Count == 1);
        vm.FilterPane.SetCategorySelected("分类一", true);
        await WaitForAsync(() => vm.Cards.Count == 1);

        vm.ClearAllFilters();
        await WaitForAsync(() => vm.Cards.Count == 2);

        Assert.Empty(vm.ActiveChips);
        Assert.True(vm.FilterPane.IsEmpty);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 40)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(40);
        }

        return condition();
    }
}
