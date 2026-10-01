using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;

namespace MijiaProductGallery.ViewModels;

/// <summary>图库加载状态。</summary>
public enum GalleryLoadState
{
    /// <summary>读取中。</summary>
    Loading,

    /// <summary>就绪（有产品可展示）。</summary>
    Ready,

    /// <summary>空图库（数据库无产品，等待初始化）。</summary>
    Empty,

    /// <summary>读取失败。</summary>
    Error,
}

/// <summary>筛选 Chip：一个激活的筛选条件（含搜索关键字与收藏模式）。</summary>
public sealed record FilterChip(string Id, string Label);

/// <summary>图库模式：收藏视图通过把 IsFavorite=true 叠加进筛选实现（复用同一条查询管线）。</summary>
public enum GalleryMode
{
    /// <summary>全部产品。</summary>
    Normal,

    /// <summary>仅收藏（查询固定叠加 IsFavorite=true）。</summary>
    Favorites,

    /// <summary>最近使用（按 UsageEvents 最新事件时间倒序的浏览视图）。</summary>
    Recent,
}

/// <summary>
/// 图库页视图模型：搜索 ∩ 筛选 ∩ 排序的组合查询（搜索 500ms 防抖、过期结果丢弃），
/// 全部过滤与排序在数据库侧执行；卡片集合替换经 UI 调度器回投。
/// 搜索与筛选均为查询入口，不修改官方数据；历史与筛选/排序状态为用户数据。
/// </summary>
public partial class GalleryViewModel : ObservableObject
{
    private const string SortSettingsKey = "Gallery.SortState";

    private readonly IProductRepository products;
    private readonly IProductQueryService queryService;
    private readonly IRecentService recentService;
    private readonly ISearchHistoryRepository searchHistory;
    private readonly IFavoritesRepository favorites;
    private readonly ISettingsRepository settings;
    private readonly ICollectionRepository? collections;
    private readonly ISyncService? syncService;
    private readonly ThumbnailLoadQueue thumbnailQueue;
    private readonly IUiDispatcher uiDispatcher;
    private readonly int debounceMilliseconds;
    private const int RecentLimit = 100;
    private int searchGeneration;
    private bool optionsLoaded;

    /// <summary>数据库当前没有任何产品（区分空图库与查询无结果）。</summary>
    private bool databaseIsEmpty;

    public GalleryViewModel(
        IProductRepository products,
        ThumbnailLoadQueue thumbnailQueue,
        IProductQueryService queryService,
        IRecentService recentService,
        ISearchHistoryRepository searchHistory,
        IFavoritesRepository favorites,
        ISettingsRepository settings,
        IUiDispatcher uiDispatcher,
        int debounceMilliseconds = 500,
        ICollectionRepository? collections = null,
        ISyncService? syncService = null)
    {
        this.products = products;
        this.thumbnailQueue = thumbnailQueue;
        this.queryService = queryService;
        this.recentService = recentService;
        this.searchHistory = searchHistory;
        this.favorites = favorites;
        this.settings = settings;
        this.collections = collections;
        this.syncService = syncService;
        this.uiDispatcher = uiDispatcher;
        this.debounceMilliseconds = debounceMilliseconds;
        FilterPane = new FilterPaneViewModel(settings);
        FilterPane.FilterChanged += OnFilterChanged;
        if (syncService is not null)
        {
            syncService.ProgressChanged += OnSyncProgress;
        }
    }

    public FilterPaneViewModel FilterPane { get; }

    public ThumbnailLoadQueue Thumbnails => thumbnailQueue;

    public ObservableCollection<ProductCard> Cards { get; } = [];

    public ObservableCollection<FilterChip> ActiveChips { get; } = [];

    /// <summary>当前显式排序；null = 默认（有关键字按命中优先级，无关键字按型号）。</summary>
    public ProductSort? Sort { get; private set; }

    /// <summary>图库模式（全部产品/仅收藏）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritesMode), nameof(IsFavoritesEmptyVisible))]
    private GalleryMode mode = GalleryMode.Normal;

    public bool IsFavoritesMode => Mode == GalleryMode.Favorites;

    /// <summary>收藏夹切换列表（首项固定为默认星标收藏；进入收藏视图时刷新）。</summary>
    public ObservableCollection<CollectionOption> Collections { get; } = [];

    /// <summary>当前选中的收藏夹（默认项 = 星标收藏语义）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoritesEmptyText))]
    private CollectionOption? selectedCollection = CollectionOption.Default;

    /// <summary>ReloadCollectionsAsync 重建列表期间抑制切换重查。</summary>
    internal bool suppressCollectionChanged;

    private HashSet<int> favoriteIds = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingVisible), nameof(IsEmptyVisible), nameof(IsErrorVisible), nameof(IsReadyVisible), nameof(IsPagerVisible), nameof(IsFavoritesEmptyVisible))]
    private GalleryLoadState state = GalleryLoadState.Loading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>结果摘要（如"空气 · 找到 32 个产品"），全量时为"共 N 个产品"。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultSummary))]
    private string? resultSummary;

    /// <summary>是否有可展示的结果摘要。</summary>
    public bool HasResultSummary => ResultSummary is not null;

    [ObservableProperty]
    private string? searchText;

    /// <summary>收藏视图空状态：无搜索词且当前合集/星标收藏没有产品。</summary>
    public bool IsFavoritesEmptyVisible =>
        Mode == GalleryMode.Favorites
        && State == GalleryLoadState.Ready
        && TotalCount == 0
        && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>收藏视图空状态文案（区分默认收藏与具体合集）。</summary>
    public string FavoritesEmptyText => CurrentCollectionName is { } name
        ? $"「{name}」还没有产品"
        : "还没有收藏的产品";

    /// <summary>是否有任何激活的筛选（含关键字）。</summary>
    public bool HasActiveChips => ActiveChips.Count > 0;

    /// <summary>浏览模式：分页（默认）/连续滚动。每次执行查询时从持久化设置解析。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible))]
    private bool isPagedMode = true;

    /// <summary>当前页码（1 起）；条件变化自动回到第 1 页。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoFirstPage), nameof(CanGoPrevPage), nameof(PageBoxText))]
    private int currentPage = 1;

    /// <summary>满足当前条件的总产品数（数据库侧 COUNT，与当前页行数无关）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible), nameof(CanGoNextPage), nameof(CanGoLastPage), nameof(IsFavoritesEmptyVisible))]
    private int totalCount;

    /// <summary>总页数（按每页数量向上取整）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible), nameof(CanGoNextPage), nameof(CanGoLastPage))]
    private int totalPages = 1;

    /// <summary>分页栏可见性：分页模式且非随机浏览且有结果。</summary>
    public bool IsPagerVisible => IsPagedMode && !IsRandomMode && State == GalleryLoadState.Ready && TotalCount > 0;

    public bool CanGoFirstPage => IsPagedMode && CurrentPage > 1;

    public bool CanGoPrevPage => CanGoFirstPage;

    public bool CanGoNextPage => IsPagedMode && CurrentPage < TotalPages;

    public bool CanGoLastPage => CanGoNextPage;

    /// <summary>页码输入框文本（双向：跳转输入 / 当前页同步）。</summary>
    [ObservableProperty]
    private string pageBoxText = "1";

    /// <summary>
    /// 提交页码输入（页码框 Enter/失焦调用，传入界面实际文本）：
    /// 合法值经 GoToPage 钳制到 1～总页数并走统一分页管线（同页不重复查询）；
    /// 非法值校正回当前页。
    /// </summary>
    public void SubmitPageText(string? raw)
    {
        if (!IsPagedMode)
        {
            PageBoxText = CurrentPage.ToString();
            return;
        }

        if (int.TryParse(
                raw?.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var page))
        {
            GoToPage(page);
        }

        // 跳转后同步为新页码；非法/越界/同页校正回当前页。
        PageBoxText = CurrentPage.ToString();
    }

    /// <summary>是否处于随机浏览模式（临时状态，不持久化）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RandomBannerText), nameof(IsPagerVisible))]
    private bool isRandomMode;

    /// <summary>随机模式本会话累计已展示产品数。</summary>
    [ObservableProperty]
    private int randomShownCount;

    /// <summary>随机模式还有更多可展示（用于禁用"换一批"）。</summary>
    [ObservableProperty]
    private bool randomExhausted;

    /// <summary>随机横幅文本。</summary>
    public string RandomBannerText => IsRandomMode
        ? RandomExhausted
            ? $"🎲 随机浏览 · 本会话已展示全部 {RandomShownCount} 个产品"
            : $"🎲 随机浏览 · 已展示 {RandomShownCount} 个产品"
        : string.Empty;

    private long? randomCursor;

    private List<string> shownModels = [];

    public bool IsLoadingVisible => State == GalleryLoadState.Loading;

    public bool IsEmptyVisible => State == GalleryLoadState.Empty;

    public bool IsErrorVisible => State == GalleryLoadState.Error;

    public bool IsReadyVisible => State == GalleryLoadState.Ready;

    /// <summary>页面入口：加载可选项与排序状态（一次），并按当前关键字、筛选与排序执行查询。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!optionsLoaded)
            {
                optionsLoaded = true;
                await FilterPane.LoadAsync(products, settings, cancellationToken);
                favoriteIds = [.. await favorites.GetFavoriteProductIdsAsync(cancellationToken)];
                Sort = await RestoreSortSafeAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            State = GalleryLoadState.Error;
            ErrorMessage = exception.Message;
            return;
        }

        databaseIsEmpty = await products.CountAsync(cancellationToken) == 0;
        searchGeneration++;
        await ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
    }

    /// <summary>
    /// 同步引擎进度回调：仅在出现数据变化（新增/下架/改名/换图等）的终态时刷新。
    /// 可选项（分类/品牌）只在启动时加载一次，同步填充或变更数据后必须重建，否则筛选面板为空。
    /// </summary>
    private void OnSyncProgress(SyncProgress progress)
    {
        if (progress.Status == SyncStatus.Running || progress.Counts is not { } counts)
        {
            return;
        }

        var changed = counts.NewCount
            + counts.DelistedCount
            + counts.CategoryChangedCount
            + counts.NameChangedCount
            + counts.ImageChangedCount
            + counts.IdReusedCount;
        if (changed == 0)
        {
            return;
        }

        uiDispatcher.Post(() => _ = RefreshAfterSyncAsync());
    }

    /// <summary>同步产生数据变化后：重建筛选可选项并重查当前视图（回到第 1 页）。</summary>
    public async Task RefreshAfterSyncAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 重新加载分类/品牌可选项；已保存的筛选选择随设置恢复，用户选择不丢失。
            await FilterPane.LoadAsync(products, settings, cancellationToken);
            favoriteIds = [.. await favorites.GetFavoriteProductIdsAsync(cancellationToken)];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 可选项刷新失败保留旧列表，下面的重查仍会执行。
        }

        ResetPaging();
        searchGeneration++;
        await ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
    }

    /// <summary>立即执行搜索（绕过防抖，用于建议提交与回车）。</summary>
    public void ApplySearchImmediate(string keyword)
    {
        ExitRandomCore();
        ResetPaging();
        // 经属性赋值会触发一次防抖路径，但世代计数使其过期，立即执行的结果最终生效。
        SearchText = keyword;
        searchGeneration++;
        _ = ExecuteAsync(keyword, searchGeneration);
    }

    /// <summary>应用显式排序（null = 恢复默认），立即执行并持久化。</summary>
    public void ApplySort(ProductSort? sort)
    {
        ExitRandomCore();
        ResetPaging();
        Sort = sort;
        _ = PersistSortSafeAsync(sort);
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>进入最近使用视图（与随机/收藏模式互斥）。</summary>
    public void EnterRecentMode()
    {
        ExitRandomCore();
        ResetPaging();
        ExitFavoritesCore();
        Mode = GalleryMode.Recent;
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>退出最近使用视图。</summary>
    public void ExitRecentMode()
    {
        if (Mode != GalleryMode.Recent)
        {
            return;
        }

        Mode = GalleryMode.Normal;
        ResetPaging();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>清空最近使用记录（仅 UsageEvents/ProductUsages；收藏与搜索历史不受影响）。</summary>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await recentService.ClearHistoryAsync(cancellationToken);
        if (Mode == GalleryMode.Recent)
        {
            searchGeneration++;
            await ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
        }
    }

    /// <summary>进入收藏视图：刷新收藏夹列表后执行查询（默认项叠加 IsFavorite，选中合集走 CollectionId）。</summary>
    public void EnterFavoritesMode()
    {
        ExitRandomCore();
        ResetPaging();
        Mode = GalleryMode.Favorites;
        searchGeneration++;
        _ = ReloadCollectionsAndExecuteAsync(searchGeneration);
    }

    /// <summary>退出收藏视图（回到全部产品）。</summary>
    public void ExitFavoritesMode()
    {
        if (Mode == GalleryMode.Normal)
        {
            return;
        }

        ExitFavoritesCore();
        ResetPaging();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    private void ExitFavoritesCore()
    {
        Mode = GalleryMode.Normal;
    }

    /// <summary>收藏夹切换：回第 1 页并按所选合集重查（默认项回到星标收藏语义）。</summary>
    partial void OnSelectedCollectionChanged(CollectionOption? value)
    {
        if (suppressCollectionChanged || Mode != GalleryMode.Favorites)
        {
            return;
        }

        ResetPaging();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>载入收藏夹列表；保持原选择，所选合集已被删除时回落默认并重查。</summary>
    /// <summary>收藏夹列表重建完成（供视图在 UI 线程同步选择框显示）。</summary>
    public event Action? CollectionsReloaded;

    public async Task ReloadCollectionsAsync(CancellationToken cancellationToken = default)
    {
        if (collections is null)
        {
            return;
        }

        var rows = await collections.GetAllAsync(cancellationToken);
        var previousId = SelectedCollection?.Id ?? 0;
        var options = new List<CollectionOption> { CollectionOption.Default };
        options.AddRange(rows.Select(row => new CollectionOption(row.Id, row.Name)));
        var match = options.FirstOrDefault(option => option.Id == previousId) ?? CollectionOption.Default;

        suppressCollectionChanged = true;
        // 先断开再选中：SelectedItem 为 OneWay 绑定，若新值与旧值同一实例，
        // 不触发 PropertyChanged 会让 ComboBox 在列表重建后错过重选（显示空白）。
        SelectedCollection = null;
        Collections.Clear();
        foreach (var option in options)
        {
            Collections.Add(option);
        }

        SelectedCollection = match;
        suppressCollectionChanged = false;
        CollectionsReloaded?.Invoke();

        if (match.Id != previousId && Mode == GalleryMode.Favorites)
        {
            // 所选合集已被删除：回落默认收藏并重查一次。
            ResetPaging();
            searchGeneration++;
            await ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
        }
    }

    /// <summary>当前收藏夹的导出条目（默认收藏=星标全集；合集=该合集成员）。只读，不影响浏览状态。</summary>
    public async Task<IReadOnlyList<CollectionExportItem>> GetExportItemsAsync(CancellationToken cancellationToken = default)
    {
        var query = SelectedCollection is { IsDefault: false } selected
            ? new ProductQuery { CollectionId = selected.Id }
            : new ProductQuery { Filter = new ProductFilter { IsFavorite = true } };
        var rows = await queryService.QueryAsync(query, cancellationToken);
        return rows
            .Select(product => new CollectionExportItem
            {
                ProductId = product.Id,
                Model = product.Model,
                Name = product.Name,
                ImagePath = product.ImagePath,
            })
            .ToList();
    }

    /// <summary>设置当前合集；合集变化时回第 1 页重查（收藏视图内）。</summary>
    private void SelectCollectionCore(CollectionOption option)
    {
        var changed = SelectedCollection?.Id != option.Id;
        suppressCollectionChanged = true;
        SelectedCollection = option;
        suppressCollectionChanged = false;
        if (changed && Mode == GalleryMode.Favorites)
        {
            ResetPaging();
            searchGeneration++;
            _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
        }
    }

    private async Task<bool> HasCollectionNameAsync(
        string trimmedName, int? excludeId, CancellationToken cancellationToken)
    {
        var rows = await collections!.GetAllAsync(cancellationToken);
        return rows.Any(row => row.Id != excludeId
            && string.Equals(row.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>新建收藏夹：空名/重名拒绝；成功后刷新列表并选中新收藏夹。</summary>
    public async Task<(bool Success, string? Error)> CreateCollectionAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (false, "收藏夹名称不能为空");
        }

        if (collections is null)
        {
            return (false, "收藏夹功能不可用");
        }

        if (await HasCollectionNameAsync(trimmed, null, cancellationToken))
        {
            return (false, $"已存在同名收藏夹「{trimmed}」");
        }

        var created = await collections.CreateAsync(trimmed, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        await ReloadCollectionsAsync(cancellationToken);
        SelectCollectionCore(new CollectionOption(created.Id, created.Name));
        return (true, null);
    }

    /// <summary>重命名收藏夹：默认收藏不可重命名；空名/重名拒绝。</summary>
    public async Task<(bool Success, string? Error)> RenameCollectionAsync(
        int collectionId, string newName, CancellationToken cancellationToken = default)
    {
        if (collectionId == 0)
        {
            return (false, "默认收藏不可重命名");
        }

        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (false, "收藏夹名称不能为空");
        }

        if (collections is null)
        {
            return (false, "收藏夹功能不可用");
        }

        if (await HasCollectionNameAsync(trimmed, collectionId, cancellationToken))
        {
            return (false, $"已存在同名收藏夹「{trimmed}」");
        }

        await collections.RenameAsync(collectionId, trimmed, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        await ReloadCollectionsAsync(cancellationToken);
        if (SelectedCollection?.Id == collectionId && Mode == GalleryMode.Favorites)
        {
            // 重命名的是当前合集：刷新 Chip 与摘要中的名称。
            ResetPaging();
            searchGeneration++;
            _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
        }

        return (true, null);
    }

    /// <summary>删除收藏夹：默认收藏不可删除；仅删除合集本身，产品与星标收藏不受影响。</summary>
    public async Task<(bool Success, string? Error)> DeleteCollectionAsync(
        int collectionId, CancellationToken cancellationToken = default)
    {
        if (collectionId == 0)
        {
            return (false, "默认收藏不可删除");
        }

        if (collections is null)
        {
            return (false, "收藏夹功能不可用");
        }

        await collections.DeleteAsync(collectionId, cancellationToken);
        await ReloadCollectionsAsync(cancellationToken);
        return (true, null);
    }

    private async Task ReloadCollectionsAndExecuteAsync(int generation)
    {
        await ReloadCollectionsAsync();
        if (generation != searchGeneration)
        {
            return;
        }

        await ExecuteAsync(SearchText ?? string.Empty, generation);
    }

    /// <summary>切换收藏状态（用户数据）：更新卡片角标；收藏视图下产品即时移出列表。</summary>
    public async Task<bool> ToggleFavoriteAsync(ProductCard card, CancellationToken cancellationToken = default)
    {
        var newState = await favorites.ToggleAsync(card.ProductId, cancellationToken);
        card.IsFavorite = newState;
        if (newState)
        {
            favoriteIds.Add(card.ProductId);
        }
        else
        {
            favoriteIds.Remove(card.ProductId);
            if (Mode == GalleryMode.Favorites)
            {
                searchGeneration++;
                _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
            }
        }

        return newState;
    }

    /// <summary>移除单个 Chip：关键字 Chip 清空搜索，筛选 Chip 反向取消对应选项。</summary>
    public void RemoveChip(string chipId)
    {
        if (chipId == "fav")
        {
            ExitFavoritesMode();
            return;
        }

        if (chipId == "collection")
        {
            SelectedCollection = CollectionOption.Default;
            return;
        }

        if (chipId == "search")
        {
            ApplySearchImmediate(string.Empty);
            return;
        }

        if (chipId.StartsWith("cat:", StringComparison.Ordinal))
        {
            FilterPane.SetCategorySelected(chipId[4..], false);
            return;
        }

        if (chipId.StartsWith("brand:", StringComparison.Ordinal))
        {
            FilterPane.SetBrandSelected(chipId[6..], false);
            return;
        }

        switch (chipId)
        {
            case "avail":
            case "image":
            case "usage":
            case "date":
                ResetDimension(chipId);
                break;
        }
    }

    /// <summary>进入随机浏览模式：从头抽取一批（关键字/筛选照常生效），排除本会话已展示。</summary>
    public void EnterRandomMode()
    {
        ExitRandomCore();
        ResetPaging();
        IsRandomMode = true;
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>换一批：游标推进到本批最大随机键之后，排除已展示型号继续抽取。</summary>
    public void RefreshRandom()
    {
        if (!IsRandomMode)
        {
            EnterRandomMode();
            return;
        }

        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>退出随机浏览：恢复正常排序与筛选视图。</summary>
    public void ExitRandomMode()
    {
        if (!IsRandomMode)
        {
            return;
        }

        ResetPaging();
        ExitRandomCore();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    private void ExitRandomCore()
    {
        IsRandomMode = false;
        randomCursor = null;
        shownModels = [];
        RandomShownCount = 0;
        RandomExhausted = false;
    }

    /// <summary>清除全部筛选与搜索（单次重查；排序为视图偏好，保留）。</summary>
    public void ClearAllFilters()
    {
        ResetPaging();
        suppressFilterChanged = true;
        SearchText = string.Empty;
        FilterPane.Reset();
        suppressFilterChanged = false;
        searchGeneration++;
        _ = ExecuteAsync(string.Empty, searchGeneration);
    }

    internal bool suppressFilterChanged;

    private void OnFilterChanged()
    {
        ExitRandomCore();
        ResetPaging();
        if (suppressFilterChanged)
        {
            return;
        }

        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    private void ResetDimension(string chipId)
    {
        ResetPaging();
        suppressFilterChanged = true;
        switch (chipId)
        {
            case "avail":
                FilterPane.Availability = AvailabilityOption.All;
                break;
            case "image":
                FilterPane.ImageOptionValue = ImageOption.All;
                break;
            case "usage":
                FilterPane.Usage = UsageRange.None;
                break;
            case "date":
                FilterPane.UpdateRange = DateRange.All;
                break;
        }

        suppressFilterChanged = false;
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    partial void OnSearchTextChanged(string? value)
    {
        _ = SearchDebouncedAsync(value ?? string.Empty);
    }

    private async Task SearchDebouncedAsync(string keyword)
    {
        ResetPaging();
        searchGeneration++;
        var generation = searchGeneration;
        await Task.Delay(debounceMilliseconds);
        if (generation != searchGeneration)
        {
            return;
        }

        await ExecuteAsync(keyword, generation);
    }

    /// <summary>从持久化设置解析每页数量（9～140，默认 21）。</summary>
    private async Task<int> ResolvePageSizeAsync(CancellationToken cancellationToken)
    {
        var value = await settings.GetValueAsync(
            AppSettingsKeys.GalleryPageSize, AppSettingsKeys.GalleryPageSizeDefault, cancellationToken);
        return Math.Clamp(value, AppSettingsKeys.GalleryPageSizeMin, AppSettingsKeys.GalleryPageSizeMax);
    }

    /// <summary>从持久化设置解析浏览模式（Paged 默认 / Continuous）。</summary>
    private async Task<bool> ResolvePagedModeAsync(CancellationToken cancellationToken)
    {
        var mode = await settings.GetValueAsync(
            AppSettingsKeys.GalleryBrowseMode, AppSettingsKeys.GalleryBrowseModeDefault, cancellationToken);
        return mode != "Continuous";
    }

    /// <summary>条件变化：回到第 1 页（搜索/筛选/排序/模式切换共用）。</summary>
    private void ResetPaging()
    {
        CurrentPage = 1;
        PageBoxText = "1";
    }

    /// <summary>页码跳转（钳制到有效范围后重新执行当前条件的对应页）。</summary>
    public void GoToPage(int page)
    {
        if (!IsPagedMode)
        {
            return;
        }

        var target = Math.Clamp(page, 1, Math.Max(1, TotalPages));
        if (target == CurrentPage)
        {
            PageBoxText = CurrentPage.ToString();
            return;
        }

        CurrentPage = target;
        PageBoxText = target.ToString();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    public void GoToFirstPage() => GoToPage(1);

    public void GoToLastPage() => GoToPage(TotalPages);

    public void GoToPrevPage() => GoToPage(CurrentPage - 1);

    public void GoToNextPage() => GoToPage(CurrentPage + 1);

    /// <summary>设置页修改每页数量/浏览模式后触发：以新参数重新执行第 1 页。</summary>
    public void OnBrowseSettingsChanged()
    {
        ResetPaging();
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    private async Task ExecuteAsync(string keyword, int generation, CancellationToken cancellationToken = default)
    {
        State = GalleryLoadState.Loading;
        ErrorMessage = null;
        try
        {
            var filter = FilterPane.BuildFilter();
            var collectionId = Mode == GalleryMode.Favorites ? SelectedCollection?.Id : null;
            if (Mode == GalleryMode.Favorites && collectionId is null or 0)
            {
                filter ??= new ProductFilter();
                filter.IsFavorite = true;
            }

            var query = new ProductQuery
            {
                Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                Filter = filter,
                Sort = Sort,
                CollectionId = collectionId is > 0 ? collectionId : null,
            };
            IReadOnlyList<Product> rows;
            IsPagedMode = await ResolvePagedModeAsync(cancellationToken);
            if (IsRandomMode)
            {
                query = new ProductQuery
                {
                    Keyword = query.Keyword,
                    Filter = query.Filter,
                    Mode = BrowseMode.Random,
                    RandomCursor = randomCursor,
                    RandomLimit = 20,
                    ExcludeModels = [.. shownModels],
                };
                rows = await queryService.QueryAsync(query, cancellationToken);

                // 游标推进到本批最大随机键；会话累计已展示；空结果表示已全部展示。
                if (generation != searchGeneration)
                {
                    return;
                }

                var last = rows.Count == 0 ? null : rows.Max(product => product.RandomKey);
                if (last is { } lastValue)
                {
                    randomCursor = lastValue + 1;
                    shownModels.AddRange(rows.Select(product => product.Model));
                    RandomShownCount = shownModels.Count;
                    RandomExhausted = false;
                }
                else
                {
                    RandomExhausted = true;
                }

                await ReplaceCardsAsync(rows, cancellationToken);
                return;
            }

            if (Mode == GalleryMode.Recent)
            {
                // 最近使用：UsageEvents 聚合视图，不走常规 Sort 管线。
                if (IsPagedMode)
                {
                    var pageSizeRecent = await ResolvePageSizeAsync(cancellationToken);
                    var recentPage = await recentService.GetRecentPageAsync(
                        (CurrentPage - 1) * pageSizeRecent, pageSizeRecent, cancellationToken);
                    if (generation != searchGeneration)
                    {
                        return;
                    }

                    TotalCount = recentPage.TotalCount;
                    TotalPages = Math.Max(1, recentPage.TotalPages(pageSizeRecent));
                    ResultSummary = TotalCount == 0
                        ? "暂无使用记录"
                        : $"最近使用 · 共 {TotalCount:N0} 个产品";
                    ActiveChips.Clear();
                    await ReplaceRecentCardsAsync(recentPage.Items, cancellationToken);
                    return;
                }

                var recentRows = await recentService.GetRecentAsync(RecentLimit, cancellationToken);
                if (generation != searchGeneration)
                {
                    return;
                }

                ResultSummary = recentRows.Count == 0 ? "暂无使用记录" : $"最近使用 · {recentRows.Count} 个产品";
                ActiveChips.Clear();
                await ReplaceRecentCardsAsync(recentRows, cancellationToken);
                return;
            }

            int totalCount;
            if (IsPagedMode)
            {
                // 分页模式：COUNT 与 Skip/Take 均在数据库侧执行，仅当前页行被物化。
                var pageSize = await ResolvePageSizeAsync(cancellationToken);
                var page = await queryService.QueryPageAsync(
                    query, (CurrentPage - 1) * pageSize, pageSize, cancellationToken);
                if (generation != searchGeneration)
                {
                    return;
                }

                var totalPages = page.TotalPages(pageSize);
                if (CurrentPage > totalPages)
                {
                    // 数据变化导致当前页失效：收敛到最后一页并重查（空结果保持第 1 页）。
                    CurrentPage = Math.Max(1, totalPages);
                    PageBoxText = CurrentPage.ToString();
                    page = await queryService.QueryPageAsync(
                        query, (CurrentPage - 1) * pageSize, pageSize, cancellationToken);
                    if (generation != searchGeneration)
                    {
                        return;
                    }
                }

                TotalCount = page.TotalCount;
                TotalPages = totalPages;
                totalCount = page.TotalCount;
                rows = page.Items;
            }
            else
            {
                rows = await queryService.QueryAsync(query, cancellationToken);
                if (generation != searchGeneration)
                {
                    return;
                }

                totalCount = rows.Count;
                // 连续模式无页码语义，但总数仍驱动空状态/摘要；TotalPages 仅作占位。
                TotalCount = totalCount;
                TotalPages = Math.Max(1, totalCount);
            }

            if (generation != searchGeneration)
            {
                return;
            }

            ResultSummary = BuildSummary(query, totalCount);
            RebuildChips(query);
            await ReplaceCardsAsync(rows, cancellationToken, favoriteIds);

            if (query.Keyword is not null)
            {
                await RecordHistorySafeAsync(query.Keyword, totalCount, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (generation != searchGeneration)
            {
                return;
            }

            State = GalleryLoadState.Error;
            ErrorMessage = exception.Message;
        }
    }

    /// <summary>当前选中的收藏夹名称（默认项为 null，走星标收藏语义）。</summary>
    public string? CurrentCollectionName =>
        Mode == GalleryMode.Favorites && SelectedCollection is { IsDefault: false } selected
            ? selected.Name
            : null;

    private string? BuildSummary(ProductQuery query, int count)
    {
        var collectionName = CurrentCollectionName;
        var prefix = collectionName is not null
            ? $"收藏夹「{collectionName}」 · "
            : query.Filter?.IsFavorite == true ? "收藏 · " : null;
        if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            if (count == 0 && (collectionName is not null || query.Filter?.IsFavorite == true))
            {
                // 收藏视图空状态由独立空状态面板表达，不再重复摘要。
                return null;
            }

            return count == 0 ? null : $"{prefix}共 {count} 个产品";
        }

        return count == 0
            ? "没有找到相关产品"
            : $"{prefix}{query.Keyword.Trim()} · 找到 {count} 个产品";
    }

    private void RebuildChips(ProductQuery query)
    {
        ActiveChips.Clear();
        if (Mode == GalleryMode.Favorites)
        {
            ActiveChips.Add(CurrentCollectionName is { } collectionName
                ? new FilterChip("collection", $"收藏夹: {collectionName}")
                : new FilterChip("fav", "收藏"));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            ActiveChips.Add(new FilterChip("search", $"搜索: {query.Keyword.Trim()}"));
        }

        var filter = query.Filter;
        if (filter is not null)
        {
            if (filter.Categories is not null)
            {
                foreach (var category in filter.Categories)
                {
                    ActiveChips.Add(new FilterChip($"cat:{category}", category));
                }
            }

            if (filter.Brands is not null)
            {
                foreach (var brand in filter.Brands)
                {
                    ActiveChips.Add(new FilterChip($"brand:{brand}", brand));
                }
            }

            if (filter.IsAvailable is { } available)
            {
                ActiveChips.Add(new FilterChip("avail", available ? "在售" : "已下架"));
            }

            if (filter.HasImage is { } hasImage)
            {
                ActiveChips.Add(new FilterChip("image", hasImage ? "有图片" : "无图片"));
            }

            switch (filter.Usage)
            {
                case UsageRange.NeverUsed:
                    ActiveChips.Add(new FilterChip("usage", "从未使用"));
                    break;
                case UsageRange.Used:
                    ActiveChips.Add(new FilterChip("usage", "已使用"));
                    break;
                case UsageRange.HighUsage:
                    ActiveChips.Add(new FilterChip("usage", "高频使用"));
                    break;
            }

            switch (filter.UpdateTime)
            {
                case DateRange.Last7Days:
                    ActiveChips.Add(new FilterChip("date", "最近 7 天"));
                    break;
                case DateRange.Last30Days:
                    ActiveChips.Add(new FilterChip("date", "最近 30 天"));
                    break;
            }
        }

        OnPropertyChanged(nameof(HasActiveChips));
    }

    /// <summary>卡片集合替换必须发生在 UI 线程。</summary>
    private Task ReplaceCardsAsync(IReadOnlyList<Product> rows, CancellationToken cancellationToken)
        => ReplaceCardsAsync(rows, cancellationToken, null);

    private Task ReplaceCardsAsync(
        IReadOnlyList<Product> rows,
        CancellationToken cancellationToken,
        HashSet<int>? favoriteIdSet)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiDispatcher.Post(() =>
        {
            try
            {
                Cards.Clear();
                foreach (var row in rows)
                {
                    var card = new ProductCard(row);
                    if (favoriteIdSet is not null && favoriteIdSet.Contains(row.Id))
                    {
                        card.IsFavorite = true;
                    }

                    Cards.Add(card);
                }

                State = Cards.Count == 0 && databaseIsEmpty ? GalleryLoadState.Empty : GalleryLoadState.Ready;
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }

    private async Task RecordHistorySafeAsync(string keyword, int resultCount, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(async () =>
            {
                await searchHistory.AddAsync(
                    keyword,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    resultCount,
                    cancellationToken);
            }, cancellationToken);
        }
        catch
        {
            // 历史记录失败不影响搜索结果。
        }
    }

    /// <summary>读取与前缀匹配的搜索建议（供 AutoSuggestBox 下拉）。</summary>
    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string? prefix, CancellationToken cancellationToken = default)
    {
        var recent = await searchHistory.GetRecentAsync(ISearchHistoryRepository.MaxEntries, cancellationToken);
        var suggestions = recent.Select(entry => entry.Query);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            suggestions = suggestions.Where(query =>
                query.Contains(prefix.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return suggestions.Take(8).ToList();
    }

    private async Task<ProductSort?> RestoreSortSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dto = await settings.GetValueAsync(SortSettingsKey, new SortStateDto(), cancellationToken);
            if (dto.Field is null)
            {
                return null;
            }

            return new ProductSort
            {
                Field = Enum.TryParse<ProductSortField>(dto.Field, ignoreCase: true, out var field) ? field : ProductSortField.Model,
                Direction = Enum.TryParse<SortDirection>(dto.Direction, ignoreCase: true, out var direction) ? direction : SortDirection.Ascending,
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistSortSafeAsync(ProductSort? sort)
    {
        try
        {
            await settings.SetValueAsync(
                SortSettingsKey,
                new SortStateDto
                {
                    Field = sort?.Field.ToString(),
                    Direction = sort?.Direction.ToString(),
                });
        }
        catch
        {
            // 持久化失败不影响排序行为。
        }
    }

    /// <summary>最近使用卡片替换：附带"最后行为 · 最后时间"信息。</summary>
    private Task ReplaceRecentCardsAsync(IReadOnlyList<RecentProduct> rows, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiDispatcher.Post(() =>
        {
            try
            {
                Cards.Clear();
                foreach (var row in rows)
                {
                    var card = new ProductCard(row.Product)
                    {
                        IsFavorite = favoriteIds.Contains(row.Product.Id),
                    };
                    var timeText = DateTimeOffset.FromUnixTimeSeconds(row.LastUsedUnix)
                        .ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    card.RecentInfo = $"{EventText(row.LastEvent)} · {timeText}";
                    Cards.Add(card);
                }

                State = Cards.Count == 0 && databaseIsEmpty ? GalleryLoadState.Empty : GalleryLoadState.Ready;
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static string EventText(UsageType type)
    {
        return type switch
        {
            UsageType.View => "查看",
            UsageType.Copy => "复制图片",
            UsageType.CopyText => "复制文本",
            UsageType.Drag => "拖拽图片",
            _ => type.ToString(),
        };
    }

    /// <summary>卡片进入视口：请求缩略图加载（由 ItemsRepeater 的 ElementPrepared 调用）。</summary>
    public void CardRealized(ProductCard card)
    {
        thumbnailQueue.Request(card);
    }

    /// <summary>排序状态持久化形态（camelCase，字段缺省表示默认排序）。</summary>
    public sealed class SortStateDto
    {
        [JsonPropertyName("field")]
        public string? Field { get; set; }

        [JsonPropertyName("direction")]
        public string? Direction { get; set; }
    }
}
