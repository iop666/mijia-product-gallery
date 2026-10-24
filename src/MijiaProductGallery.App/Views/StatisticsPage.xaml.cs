using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>统计页：使用计数、图库概览、大类分布与最常使用产品列表。</summary>
public sealed partial class StatisticsPage : Page
{
    private readonly StatisticsViewModel vm;
    private readonly ISyncService? sync;
    private bool syncSubscribed;

    public StatisticsViewModel Vm => vm;

    public StatisticsPage(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        sync = App.Services.GetService<ISyncService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>导航到统计页时刷新数据（页面实例复用，Loaded 不会每次触发，统一由路由调用）。</summary>
    public void ActivateView()
    {
        _ = vm.LoadAsync();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = vm.LoadAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sync is not null && !syncSubscribed)
        {
            sync.ProgressChanged += OnSyncProgress;
            syncSubscribed = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (syncSubscribed)
        {
            sync!.ProgressChanged -= OnSyncProgress;
            syncSubscribed = false;
        }
    }

    /// <summary>同步完成后统计数字已过时：页面可见时自动重新加载。</summary>
    private void OnSyncProgress(SyncProgress progress)
    {
        if (progress.Status is SyncStatus.Success or SyncStatus.Failed)
        {
            DispatcherQueue.TryEnqueue(() => _ = vm.LoadAsync());
        }
    }
}
