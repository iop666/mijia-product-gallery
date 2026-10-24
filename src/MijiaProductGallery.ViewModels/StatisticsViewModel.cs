using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.ViewModels;

/// <summary>分类占比条目：数量与占图库总数的百分比（供进度条与文字展示）。</summary>
public sealed record CategoryStatItem(string Category, int Count, double Percent)
{
    public string PercentText => $"{Percent:0.#}%";

    public string SummaryText => $"{Count} 款 · {PercentText}";
}

/// <summary>最常使用条目：带名次，便于列表直接展示。</summary>
public sealed record TopProductItem(int Rank, string Name, string Model, int TotalUseCount);

/// <summary>统计页视图模型：今日/本周/本月/累计四项计数与最常使用产品列表。</summary>
public partial class StatisticsViewModel : ObservableObject
{
    private readonly IUsageStatisticsService statisticsService;
    private bool loading;

    public StatisticsViewModel(IUsageStatisticsService statisticsService)
    {
        this.statisticsService = statisticsService;
    }

    /// <summary>首次数据尚未返回时为 true（页面显示进度环，避免闪出全 0）。</summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>加载失败信息；null 表示正常。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    private long todayCount;

    [ObservableProperty]
    private long weekCount;

    [ObservableProperty]
    private long monthCount;

    [ObservableProperty]
    private long totalCount;

    /// <summary>最近一次成功刷新的时间描述。</summary>
    [ObservableProperty]
    private string? lastUpdatedText;

    [ObservableProperty]
    private IReadOnlyList<TopProductItem> topProducts = [];

    /// <summary>最常使用列表是否为空（供空态提示直绑）。</summary>
    public bool TopProductsEmpty => TopProducts.Count == 0;

    /// <summary>图库概览：产品总数。</summary>
    [ObservableProperty]
    private int galleryTotalCount;

    /// <summary>图库概览：有图片文件数量。</summary>
    [ObservableProperty]
    private int galleryWithImageCount;

    /// <summary>图库概览：无图片数量。</summary>
    [ObservableProperty]
    private int galleryWithoutImageCount;

    /// <summary>图库概览：官网已移除数量（仍计入产品总数）。</summary>
    [ObservableProperty]
    private int galleryRemovedCount;

    /// <summary>图库概览：各大类数量（按数量降序，含占比）。</summary>
    [ObservableProperty]
    private IReadOnlyList<CategoryStatItem> categoryStats = [];

    /// <summary>大类分布是否为空（供空态提示直绑）。</summary>
    public bool CategoryStatsEmpty => CategoryStats.Count == 0;

    /// <summary>刷新统计（进入页面或请求刷新时调用）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (loading)
        {
            return;
        }

        loading = true;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var statistics = await statisticsService.GetStatisticsAsync(cancellationToken: cancellationToken);
            TodayCount = statistics.TodayCount;
            WeekCount = statistics.WeekCount;
            MonthCount = statistics.MonthCount;
            TotalCount = statistics.TotalCount;
            TopProducts = statistics.TopProducts
                .Select((item, index) => new TopProductItem(index + 1, item.Name, item.Model, item.TotalUseCount))
                .ToList();
            OnPropertyChanged(nameof(TopProductsEmpty));

            var overview = await statisticsService.GetGalleryOverviewAsync(cancellationToken);
            GalleryTotalCount = overview.TotalCount;
            GalleryWithImageCount = overview.WithImageCount;
            GalleryWithoutImageCount = overview.WithoutImageCount;
            GalleryRemovedCount = overview.RemovedCount;
            CategoryStats = overview.Categories
                .Select(category => new CategoryStatItem(
                    category.Category,
                    category.Count,
                    overview.TotalCount <= 0 ? 0 : Math.Round(100.0 * category.Count / overview.TotalCount, 1)))
                .ToList();
            OnPropertyChanged(nameof(CategoryStatsEmpty));

            LastUpdatedText = $"更新于 {DateTime.Now:HH:mm}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = $"统计数据加载失败：{exception.Message}";
        }
        finally
        {
            loading = false;
            IsLoading = false;
        }
    }
}
