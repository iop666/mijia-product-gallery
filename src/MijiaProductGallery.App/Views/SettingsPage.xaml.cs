using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>设置页：常规/图库/行为/数据/同步/关于六区。</summary>
public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel vm;
    private readonly IBackupService backupService;
    private readonly IThumbnailService thumbnailService;
    private bool suppressSelectionEvents;
    private bool busy;
    private bool subscribed;

    public SettingsPage(SettingsViewModel viewModel, IBackupService backupService, IThumbnailService thumbnailService)
    {
        InitializeComponent();
        vm = viewModel;
        this.backupService = backupService;
        this.thumbnailService = thumbnailService;
        DataContext = vm;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (subscribed)
        {
            vm.SaveFailed -= OnSettingsSaveFailed;
            subscribed = false;
        }
    }

    private void OnSettingsSaveFailed(string message)
    {
        DispatcherQueue.TryEnqueue(() => ShowNotice(message, InfoBarSeverity.Error));
    }

    /// <summary>数据操作互斥：备份/恢复/删除/清空类操作执行期间禁用入口，避免并发写库。</summary>
    private void SetBusy(bool value)
    {
        busy = value;
        BackupNowButton.IsEnabled = !value;
        RestoreBackupButton.IsEnabled = !value;
        DeleteBackupButton.IsEnabled = !value;
        RefreshBackupsButton.IsEnabled = !value;
        ClearHistoryButton.IsEnabled = !value;
        ClearThumbCacheButton.IsEnabled = !value;
        BackupFilesBox.IsEnabled = !value;
    }

    private void OnComboOpening(object? sender, object e)
    {
        if (sender is ComboBox combo)
        {
            combo.RequestedTheme = MijiaProductGallery.App.ThemeManager.ToElementTheme(
                MijiaProductGallery.App.ThemeManager.CurrentTheme);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!subscribed)
        {
            vm.SaveFailed += OnSettingsSaveFailed;
            subscribed = true;
        }

        // 先加载持久化设置，再回填各控件显示，保证下拉框反映真实持久化值。
        try
        {
            await vm.LoadAsync();
        }
        catch (Exception exception)
        {
            ShowNotice($"读取设置失败：{exception.Message}", InfoBarSeverity.Error);
            return;
        }

        suppressSelectionEvents = true;
        ThemeBox.SelectedIndex = vm.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            "Gray" => 3,
            _ => 0,
        };
        LaunchViewBox.SelectedIndex = vm.LaunchView switch
        {
            "Favorites" => 1,
            "Recent" => 2,
            _ => 0,
        };
        BrowseModeBox.SelectedIndex = vm.BrowseMode switch
        {
            "Continuous" => 1,
            _ => 0,
        };
        UpdatePageSizeAvailability();
        PageSizeBox.SelectedIndex = vm.PageSize switch
        {
            9 => 0,
            12 => 1,
            15 => 2,
            21 => 3,
            28 => 4,
            35 => 5,
            56 => 6,
            70 => 7,
            105 => 8,
            140 => 9,
            _ => 3,
        };
        suppressSelectionEvents = false;
        try
        {
            await RefreshBackupListAsync();
        }
        catch (Exception exception)
        {
            ShowNotice($"读取备份列表失败：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task RefreshBackupListAsync()
    {
        var selectedName = (BackupFilesBox.SelectedItem as BackupEntry)?.FileName;
        var backups = await backupService.ListBackupsAsync();
        suppressSelectionEvents = true;
        BackupFilesBox.ItemsSource = backups;
        BackupFilesBox.SelectedItem = backups.FirstOrDefault(entry => entry.FileName == selectedName)
            ?? backups.FirstOrDefault();
        suppressSelectionEvents = false;
    }

    /// <summary>按浏览模式同步"每页显示数量"可用性（连续滚动模式下不参与任何查询逻辑）。</summary>
    private void UpdatePageSizeAvailability()
    {
        var paged = vm.IsPagedBrowseMode;
        PageSizeBox.IsEnabled = paged;
        PageSizeHeader.Text = paged
            ? "每页显示数量（9–140）"
            : "每页显示数量（连续滚动模式下不可用）";
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        vm.Theme = ((ComboBoxItem)((ComboBox)sender).SelectedItem).Content switch
        {
            "浅色" => "Light",
            "深色" => "Dark",
            "灰色" => "Gray",
            _ => "System",
        };
    }

    private void OnBrowseModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        vm.BrowseMode = ((ComboBoxItem)((ComboBox)sender).SelectedItem).Content switch
        {
            "连续滚动" => "Continuous",
            _ => "Paged",
        };
        UpdatePageSizeAvailability();
    }

    private void OnPageSizeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        if (int.TryParse(
                ((ComboBoxItem)((ComboBox)sender).SelectedItem).Content as string,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var size))
        {
            vm.PageSize = size;
        }
    }

    private void OnLaunchViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        vm.LaunchView = ((ComboBoxItem)((ComboBox)sender).SelectedItem).Content switch
        {
            "收藏视图" => "Favorites",
            "最近使用" => "Recent",
            _ => "Gallery",
        };
    }



    private async void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Title = "清空最近使用记录？",
            Content = "将删除全部使用事件与计数。收藏不会受到影响。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await vm.ClearHistoryAsync();
            ShowNotice("最近使用记录已清空。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"清空失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshBackupsClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await RefreshBackupListAsync();
            ShowNotice("备份列表已刷新。", InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            ShowNotice($"读取备份列表失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnBackupNowClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var entry = await backupService.CreateBackupAsync();
            await RefreshBackupListAsync();
            BackupFilesBox.SelectedItem = entry;
            ShowNotice($"备份已创建：{entry.FileName}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"备份失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        if (BackupFilesBox.SelectedItem is not BackupEntry backup)
        {
            ShowNotice("请先选择要恢复的备份。", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Title = "恢复数据库？",
            Content = $"将用备份「{backup.FileName}」替换当前数据库，恢复前会自动保存当前库的安全快照。收藏与使用记录随备份一起回滚。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await backupService.RestoreAsync(backup.FileName);
            // 恢复后内存中的卡片/统计仍是旧库内容：立即重新加载，避免界面与数据库不一致。
            await App.Services.GetRequiredService<GalleryViewModel>().LoadAsync();
            ShowNotice("恢复完成，图库已重新加载。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"恢复失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        if (BackupFilesBox.SelectedItem is not BackupEntry backup)
        {
            ShowNotice("请先选择要删除的备份。", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Title = "删除备份？",
            Content = $"将永久删除备份「{backup.FileName}」，此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await backupService.DeleteBackupAsync(backup.FileName);
            await RefreshBackupListAsync();
            ShowNotice($"已删除备份：{backup.FileName}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"删除失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnClearThumbCacheClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await thumbnailService.DeleteAllThumbnailsAsync();
            ShowNotice("缩略图缓存已清空，浏览图库时会自动重建。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"清空缩略图缓存失败：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = severity == InfoBarSeverity.Error ? "操作失败" : "提示";
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }
}
