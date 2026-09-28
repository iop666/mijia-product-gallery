using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

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

/// <summary>
/// 图库页视图模型：全部产品或按关键字搜索（500ms 防抖、过期结果丢弃），
/// 一次读取轻量产品行，展示交给虚拟化布局，图片按视口按需经队列加载。
/// 搜索只是图库的查询入口：不修改官方数据；搜索历史为用户数据。
/// </summary>
public partial class GalleryViewModel : ObservableObject
{
    private readonly IProductRepository products;
    private readonly ISearchService search;
    private readonly ISearchHistoryRepository searchHistory;
    private readonly ThumbnailLoadQueue thumbnailQueue;
    private readonly IUiDispatcher uiDispatcher;
    private readonly int debounceMilliseconds;
    private int searchGeneration;

    public GalleryViewModel(
        IProductRepository products,
        ThumbnailLoadQueue thumbnailQueue,
        ISearchService search,
        ISearchHistoryRepository searchHistory,
        IUiDispatcher uiDispatcher,
        int debounceMilliseconds = 500)
    {
        this.products = products;
        this.thumbnailQueue = thumbnailQueue;
        this.search = search;
        this.searchHistory = searchHistory;
        this.uiDispatcher = uiDispatcher;
        this.debounceMilliseconds = debounceMilliseconds;
    }

    public ThumbnailLoadQueue Thumbnails => thumbnailQueue;

    public ObservableCollection<ProductCard> Cards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingVisible), nameof(IsEmptyVisible), nameof(IsErrorVisible), nameof(IsReadyVisible))]
    private GalleryLoadState state = GalleryLoadState.Loading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>结果摘要（如"空气 · 找到 32 个产品"），全量时为"共 N 个产品"。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultSummary))]
    private string? resultSummary;

    /// <summary>是否有结果摘要可显示。</summary>
    public bool HasResultSummary => !string.IsNullOrEmpty(ResultSummary);

    [ObservableProperty]
    private string? searchText;

    public bool IsLoadingVisible => State == GalleryLoadState.Loading;

    public bool IsEmptyVisible => State == GalleryLoadState.Empty;

    public bool IsErrorVisible => State == GalleryLoadState.Error;

    public bool IsReadyVisible => State == GalleryLoadState.Ready;

    /// <summary>页面入口：按当前关键字加载（空关键字=全部）。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        searchGeneration++;
        return ExecuteAsync(SearchText ?? string.Empty, searchGeneration, cancellationToken);
    }

    /// <summary>立即执行搜索（绕过防抖，用于建议提交与回车）。</summary>
    public void ApplySearchImmediate(string keyword)
    {
        // 经属性赋值会触发一次防抖路径，但世代计数使其过期，立即执行的结果最终生效。
        SearchText = keyword;
        searchGeneration++;
        _ = ExecuteAsync(keyword, searchGeneration);
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
            IReadOnlyList<Product> rows;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                rows = await products.GetAllAsync(cancellationToken);
                ResultSummary = rows.Count == 0 ? null : $"共 {rows.Count} 个产品";
            }
            else
            {
                rows = await search.SearchAsync(keyword, cancellationToken);
                ResultSummary = rows.Count == 0
                    ? "没有找到相关产品"
                    : $"{keyword.Trim()} · 找到 {rows.Count} 个产品";
            }

            if (generation != searchGeneration)
            {
                return;
            }

            await ReplaceCardsAsync(rows, cancellationToken);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                await RecordHistorySafeAsync(keyword.Trim(), rows.Count, cancellationToken);
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

    /// <summary>卡片进入视口：请求缩略图加载（由 ItemsRepeater 的 ElementPrepared 调用）。</summary>
    public void CardRealized(ProductCard card)
    {
        thumbnailQueue.Request(card);
    }
}
