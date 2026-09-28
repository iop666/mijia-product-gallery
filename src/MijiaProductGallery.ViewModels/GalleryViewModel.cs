using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
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

/// <summary>筛选 Chip：一个激活的筛选条件（含搜索关键字）。</summary>
public sealed record FilterChip(string Id, string Label);

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
    private readonly ISearchHistoryRepository searchHistory;
    private readonly ISettingsRepository settings;
    private readonly ThumbnailLoadQueue thumbnailQueue;
    private readonly IUiDispatcher uiDispatcher;
    private readonly int debounceMilliseconds;
    private int searchGeneration;
    private bool optionsLoaded;

    public GalleryViewModel(
        IProductRepository products,
        ThumbnailLoadQueue thumbnailQueue,
        IProductQueryService queryService,
        ISearchHistoryRepository searchHistory,
        ISettingsRepository settings,
        IUiDispatcher uiDispatcher,
        int debounceMilliseconds = 500)
    {
        this.products = products;
        this.thumbnailQueue = thumbnailQueue;
        this.queryService = queryService;
        this.searchHistory = searchHistory;
        this.settings = settings;
        this.uiDispatcher = uiDispatcher;
        this.debounceMilliseconds = debounceMilliseconds;
        FilterPane = new FilterPaneViewModel(settings);
        FilterPane.FilterChanged += OnFilterChanged;
    }

    public FilterPaneViewModel FilterPane { get; }

    public ThumbnailLoadQueue Thumbnails => thumbnailQueue;

    public ObservableCollection<ProductCard> Cards { get; } = [];

    public ObservableCollection<FilterChip> ActiveChips { get; } = [];

    /// <summary>当前显式排序；null = 默认（有关键字按命中优先级，无关键字按型号）。</summary>
    public ProductSort? Sort { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingVisible), nameof(IsEmptyVisible), nameof(IsErrorVisible), nameof(IsReadyVisible))]
    private GalleryLoadState state = GalleryLoadState.Loading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>结果摘要（如"空气 · 找到 32 个产品"），全量时为"共 N 个产品"。</summary>
    [ObservableProperty]
    private string? resultSummary;

    [ObservableProperty]
    private string? searchText;

    /// <summary>是否有任何激活的筛选（含关键字）。</summary>
    public bool HasActiveChips => ActiveChips.Count > 0;

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

        searchGeneration++;
        await ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
    }

    /// <summary>立即执行搜索（绕过防抖，用于建议提交与回车）。</summary>
    public void ApplySearchImmediate(string keyword)
    {
        // 经属性赋值会触发一次防抖路径，但世代计数使其过期，立即执行的结果最终生效。
        SearchText = keyword;
        searchGeneration++;
        _ = ExecuteAsync(keyword, searchGeneration);
    }

    /// <summary>应用显式排序（null = 恢复默认），立即执行并持久化。</summary>
    public void ApplySort(ProductSort? sort)
    {
        Sort = sort;
        _ = PersistSortSafeAsync(sort);
        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    /// <summary>移除单个 Chip：关键字 Chip 清空搜索，筛选 Chip 反向取消对应选项。</summary>
    public void RemoveChip(string chipId)
    {
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

    /// <summary>清除全部筛选与搜索（单次重查；排序为视图偏好，保留）。</summary>
    public void ClearAllFilters()
    {
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
        if (suppressFilterChanged)
        {
            return;
        }

        searchGeneration++;
        _ = ExecuteAsync(SearchText ?? string.Empty, searchGeneration);
    }

    private void ResetDimension(string chipId)
    {
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
        searchGeneration++;
        var generation = searchGeneration;
        await Task.Delay(debounceMilliseconds);
        if (generation != searchGeneration)
        {
            return;
        }

        await ExecuteAsync(keyword, generation);
    }

    private async Task ExecuteAsync(string keyword, int generation, CancellationToken cancellationToken = default)
    {
        State = GalleryLoadState.Loading;
        ErrorMessage = null;
        try
        {
            var query = new ProductQuery
            {
                Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                Filter = FilterPane.BuildFilter(),
                Sort = Sort,
            };
            var rows = await queryService.QueryAsync(query, cancellationToken);

            if (generation != searchGeneration)
            {
                return;
            }

            ResultSummary = BuildSummary(query, rows.Count);
            RebuildChips(query);
            await ReplaceCardsAsync(rows, cancellationToken);

            if (query.Keyword is not null)
            {
                await RecordHistorySafeAsync(query.Keyword, rows.Count, cancellationToken);
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

    private static string? BuildSummary(ProductQuery query, int count)
    {
        if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            return count == 0 ? null : $"共 {count} 个产品";
        }

        return count == 0
            ? "没有找到相关产品"
            : $"{query.Keyword.Trim()} · 找到 {count} 个产品";
    }

    private void RebuildChips(ProductQuery query)
    {
        ActiveChips.Clear();
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
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiDispatcher.Post(() =>
        {
            try
            {
                Cards.Clear();
                foreach (var row in rows)
                {
                    Cards.Add(new ProductCard(row));
                }

                State = Cards.Count == 0 ? GalleryLoadState.Empty : GalleryLoadState.Ready;
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
