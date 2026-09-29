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

    public SettingsPage(SettingsViewModel viewModel, IBackupService backupService, IThumbnailService thumbnailService)
    {
        InitializeComponent();
        vm = viewModel;
        this.backupService = backupService;
        this.thumbnailService = thumbnailService;
        DataContext = vm;
        Loaded += OnLoaded;
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
        // 先加载持久化设置，再回填各控件显示，保证下拉框反映真实持久化值。
        await vm.LoadAsync();
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
        await RefreshBackupListAsync();
    }

    private async Task RefreshBackupListAsync()
    {
        var backups = await backupService.ListBackupsAsync();
        suppressSelectionEvents = true;
        BackupFilesBox.ItemsSource = backups.Select(entry => entry.FileName).ToList();
        suppressSelectionEvents = false;
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
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清空最近使用记录？",
            Content = "将删除全部使用事件与计数。收藏不会受到影响。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await vm.ClearHistoryAsync();
        }
    }

    private void OnBackupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void OnRefreshBackupsClick(object sender, RoutedEventArgs e)
    {
        _ = RefreshBackupListAsync();
    }

    private async void OnBackupNowClick(object sender, RoutedEventArgs e)
    {
        var entry = await backupService.CreateBackupAsync();
        await RefreshBackupListAsync();
        ShowNotice($"备份已创建：{entry.FileName}", InfoBarSeverity.Success);
    }

    private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupFilesBox.SelectedItem is not string fileName)
        {
            ShowNotice("请先选择要恢复的备份。", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "恢复数据库？",
            Content = $"将用备份「{fileName}」替换当前数据库，恢复前会自动保存当前库的安全快照。收藏与使用记录随备份一起回滚。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await backupService.RestoreAsync(fileName);
            ShowNotice("恢复完成。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowNotice($"恢复失败：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupFilesBox.SelectedItem is not string fileName)
        {
            ShowNotice("请先选择要删除的备份。", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除备份？",
            Content = $"将永久删除备份「{fileName}」，此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await backupService.DeleteBackupAsync(fileName);
            await RefreshBackupListAsync();
        }
    }

    private async void OnClearThumbCacheClick(object sender, RoutedEventArgs e)
    {
        await thumbnailService.DeleteAllThumbnailsAsync();
        ShowNotice("缩略图缓存已清空，浏览图库时会自动重建。", InfoBarSeverity.Success);
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = severity == InfoBarSeverity.Error ? "操作失败" : "提示";
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }
}
