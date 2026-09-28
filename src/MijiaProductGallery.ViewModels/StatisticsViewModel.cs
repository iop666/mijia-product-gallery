using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>统计页视图模型：今日/本周/本月/累计四项计数与最常使用产品列表。</summary>
public partial class StatisticsViewModel : ObservableObject
{
    private readonly IUsageStatisticsService statisticsService;

    public StatisticsViewModel(IUsageStatisticsService statisticsService)
    {
        this.statisticsService = statisticsService;
    }

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

    /// <summary>刷新统计（进入页面或请求刷新时调用）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var statistics = await statisticsService.GetStatisticsAsync(cancellationToken: cancellationToken);
        TodayCount = statistics.TodayCount;
        WeekCount = statistics.WeekCount;
        MonthCount = statistics.MonthCount;
        TotalCount = statistics.TotalCount;
        TopProducts = statistics.TopProducts;
        OnPropertyChanged(nameof(TopProductsEmpty));
    }
}
