using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

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

    [ObservableProperty]
    private IReadOnlyList<TopUsedProduct> topProducts = [];

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

    /// <summary>图库概览：各大类数量（按数量降序）。</summary>
    [ObservableProperty]
    private IReadOnlyList<CategoryCount> categoryStats = [];

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
            TopProducts = statistics.TopProducts;
            OnPropertyChanged(nameof(TopProductsEmpty));

            var overview = await statisticsService.GetGalleryOverviewAsync(cancellationToken);
            GalleryTotalCount = overview.TotalCount;
            GalleryWithImageCount = overview.WithImageCount;
            GalleryWithoutImageCount = overview.WithoutImageCount;
            GalleryRemovedCount = overview.RemovedCount;
            CategoryStats = overview.Categories;
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
