using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>同步中心页：状态/阶段/上次同步/变更统计展示与立即同步、取消、自动频率设置。</summary>
public sealed partial class SyncCenterPage : Page
{
    private readonly SyncCenterViewModel vm;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer refreshTimer;
    private bool settingsLoaded;

    public SyncCenterViewModel Vm => vm;

    public SyncCenterPage(SyncCenterViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += async (_, _) => await vm.RefreshAsync();
    }

    /// <summary>导航回同步中心时恢复轮询与刷新。</summary>
    public void ActivateView()
    {
        _ = InitializeAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (!settingsLoaded)
        {
            settingsLoaded = true;
            await vm.LoadSettingsAsync();
        }

        await vm.RefreshAsync();
        refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
    }

    private async void OnSyncNowClick(object sender, RoutedEventArgs e)
    {
        await vm.SyncNowAsync();
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        await vm.CancelAsync();
    }

    private void OnAutoIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var interval = ((ComboBox)sender).SelectedIndex switch
        {
            0 => SyncAutoInterval.Off,
            1 => SyncAutoInterval.Startup,
            2 => SyncAutoInterval.Hours6,
            3 => SyncAutoInterval.Daily,
            _ => SyncAutoInterval.Weekly,
        };
        _ = vm.SetAutoIntervalAsync(interval);
    }
}
