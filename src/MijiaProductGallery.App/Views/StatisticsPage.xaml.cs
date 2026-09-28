using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>统计页：今日/本周/本月/累计四项计数与最常使用产品列表（无图表）。</summary>
public sealed partial class StatisticsPage : Page
{
    private readonly StatisticsViewModel vm;

    public StatisticsViewModel Vm => vm;

    public StatisticsPage(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    /// <summary>导航回统计页时刷新数据。</summary>
    public void ActivateView()
    {
        _ = vm.LoadAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await vm.LoadAsync();
    }
}
