using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>设置页：常规/图库/行为/数据/同步/关于六区。</summary>
public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel vm;
    private bool suppressSelectionEvents;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        suppressSelectionEvents = true;
        ThemeBox.SelectedIndex = vm.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        LaunchViewBox.SelectedIndex = vm.LaunchView switch
        {
            "Favorites" => 1,
            "Recent" => 2,
            _ => 0,
        };
        SyncIntervalBox.SelectedIndex = vm.AutoInterval switch
        {
            SyncAutoInterval.Off => 0,
            SyncAutoInterval.Startup => 1,
            SyncAutoInterval.Hours6 => 2,
            SyncAutoInterval.Weekly => 4,
            _ => 3,
        };
        suppressSelectionEvents = false;
        await vm.LoadAsync();
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
            _ => "System",
        };
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

    private async void OnThumbApplyClick(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ThumbEdgeBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var edge)
            && int.TryParse(ThumbQualityBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var quality))
        {
            await vm.UpdateThumbnailOptionsAsync(edge, quality);
        }
    }

    private void OnSyncIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        vm.AutoInterval = ((ComboBox)sender).SelectedIndex switch
        {
            0 => SyncAutoInterval.Off,
            1 => SyncAutoInterval.Startup,
            2 => SyncAutoInterval.Hours6,
            4 => SyncAutoInterval.Weekly,
            _ => SyncAutoInterval.Daily,
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
}
