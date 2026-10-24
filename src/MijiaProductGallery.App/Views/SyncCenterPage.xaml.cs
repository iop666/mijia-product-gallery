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
    private bool refreshing;
    private bool initializing;
    private int idleTicks;

    public SyncCenterViewModel Vm => vm;

    public SyncCenterPage(SyncCenterViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        Unloaded += OnUnloaded;
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += OnRefreshTick;
    }

    /// <summary>导航到同步中心时恢复轮询与刷新（页面实例复用，统一由路由调用）。</summary>
    public void ActivateView()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (initializing)
        {
            return;
        }

        initializing = true;
        try
        {
            if (!settingsLoaded)
            {
                await vm.LoadSettingsAsync();
                settingsLoaded = true;
            }

            await SafeRefreshAsync();
            refreshTimer.Start();
        }
        finally
        {
            initializing = false;
        }
    }

    private async void OnRefreshTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        // 空闲时降低轮询频率；正在刷新时跳过本 tick，避免请求堆叠。
        if (!vm.IsRunning && ++idleTicks % 5 != 0)
        {
            return;
        }

        await SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync()
    {
        if (refreshing)
        {
            return;
        }

        refreshing = true;
        try
        {
            await vm.RefreshAsync();
        }
        catch (Exception exception)
        {
            vm.ErrorMessage = $"读取同步状态失败：{exception.Message}";
        }
        finally
        {
            refreshing = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
    }

    private async void OnSyncNowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await vm.SyncNowAsync();
        }
        catch (OperationCanceledException)
        {
            await SafeRefreshAsync();
        }
        catch (Exception exception)
        {
            vm.ErrorMessage = $"同步失败：{exception.Message}";
            await SafeRefreshAsync();
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await vm.CancelAsync();
        }
        catch (Exception exception)
        {
            vm.ErrorMessage = $"取消失败：{exception.Message}";
        }
    }

    private void OnComboOpening(object? sender, object e)
    {
        if (sender is ComboBox combo)
        {
            combo.RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme);
        }
    }

    private void OnAutoIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsLoaded)
        {
            // 页面尚未读完持久化设置：此时的选中变化来自初始绑定，不回写。
            return;
        }

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
