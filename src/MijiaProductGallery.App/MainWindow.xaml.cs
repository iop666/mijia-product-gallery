using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.App.Views;
using MijiaProductGallery.Core;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App;

/// <summary>
/// 主窗口：Fluent NavigationView 壳，标题栏内容延伸集成；
/// 启动时按数据库状态路由到初始化页或图库页。
/// </summary>
public sealed partial class MainWindow : Window
{
    private GalleryViewModel? galleryViewModel;
    private bool galleryWired;
    private InitializationViewModel? initializationViewModel;
    private StatisticsViewModel? statisticsViewModel;
    private GalleryPage? galleryPage;
    private InitializationPage? initializationPage;
    private Views.StatisticsPage? statisticsPage;
    private SyncCenterViewModel? syncCenterViewModel;
    private Views.SyncCenterPage? syncCenterPage;
    private SettingsViewModel? settingsViewModel;
    private Views.SettingsPage? settingsPage;

    public MainWindow()
    {
        InitializeComponent();
        Title = "米家产品示例图库";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
        ApplyTitleBarTheme(App.InitialTheme);
        App.Services.GetRequiredService<SettingsViewModel>().ThemeChanged += OnThemeChanged;
        Nav.Loaded += OnLoaded;
        Nav.SizeChanged += OnNavSizeChanged;
    }

    /// <summary>窗口最小逻辑尺寸 900×600：过小时回弹到最小尺寸，保证布局稳定。</summary>
    private void OnNavSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 900 && e.NewSize.Height >= 600)
        {
            return;
        }

        var scale = Nav.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)Math.Max(900 * scale, 1);
        var height = (int)Math.Max(600 * scale, 1);
        var size = AppWindow.Size;
        if (size.Width != width || size.Height != height)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
    }

    /// <summary>默认标题栏不跟随应用主题，三种主题模式显式设置标题栏与按钮颜色。</summary>
    private void ApplyTitleBarTheme(string theme)
    {
        var bar = AppWindow.TitleBar;
        if (theme == "Dark")
        {
            ApplyTitleBarColors(bar, 0xFF202020, 0xFF303030);
        }
        else if (theme == "Gray")
        {
            ApplyTitleBarColors(bar, 0xFF3B3B3B, 0xFF4A4A4A);
        }
        else
        {
            ApplyTitleBarColors(bar, 0xFFFFFFFF, 0xFFE5E5E5);
        }
    }

    private static void ApplyTitleBarColors(
        Microsoft.UI.Windowing.AppWindowTitleBar bar,
        uint background,
        uint hover)
    {
        var bg = Windows.UI.Color.FromArgb(
            (byte)(background >> 24), (byte)(background >> 16), (byte)(background >> 8), (byte)background);
        var hv = Windows.UI.Color.FromArgb(
            (byte)(hover >> 24), (byte)(hover >> 16), (byte)(hover >> 8), (byte)hover);
        var fg = Luminance(background) < 128
            ? Windows.UI.Color.FromArgb(255, 240, 240, 240)
            : Windows.UI.Color.FromArgb(255, 26, 26, 26);
        bar.ForegroundColor = fg;
        bar.BackgroundColor = bg;
        bar.ButtonForegroundColor = fg;
        bar.ButtonBackgroundColor = bg;
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonHoverBackgroundColor = hv;
        bar.ButtonPressedBackgroundColor = bg;
        bar.ButtonInactiveBackgroundColor = bg;
        bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 150, 150, 150);
    }

    private static int Luminance(uint argb)
    {
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        return (r * 299 + g * 587 + b * 114) / 1000;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RouteAsync();
    }

    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!args.CheckCurrent())
        {
            return;
        }

        var vm = galleryViewModel ??= App.Services.GetRequiredService<GalleryViewModel>();
        SearchBox.ItemsSource = await vm.GetSearchSuggestionsAsync(sender.Text);
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var vm = galleryViewModel ??= App.Services.GetRequiredService<GalleryViewModel>();
        vm.ApplySearchImmediate(args.QueryText ?? string.Empty);
    }

    private void OnCtrlFInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    /// <summary>搜索关键字在视图模型侧被清除（Chip 删除/清除全部）时同步输入框文本。</summary>
    private void OnGalleryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.SearchText))
        {
            var text = galleryViewModel?.SearchText ?? string.Empty;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (SearchBox.Text != text)
                {
                    SearchBox.Text = text;
                }
            });
        }
    }

    private async Task RouteAsync()
    {
        // 启动顺序：先建库/迁移，再判断是否需要初始化。
        var dbInitializer = App.Services.GetRequiredService<DbInitializer>();
        await dbInitializer.InitializeAsync();

        var products = App.Services.GetRequiredService<IProductRepository>();
        var hasProducts = await products.CountAsync() > 0;
        if (hasProducts)
        {
            ShowGallery();
        }
        else
        {
            ShowInitialization();
        }
    }

    /// <summary>主题实时切换：窗口实例保持不变，当前页面与主要 UI 状态全部保留。
    /// 深色↔灰色元素主题相同、无变更信号，借一次浅色过渡强制全树主题资源重解析；
    /// 弹窗/对话框在打开时经 ThemeManager 对齐主题。</summary>
    private void OnThemeChanged(string theme)
    {
        DispatcherQueue.TryEnqueue(() => _ = ApplyThemeLiveAsync(theme));
    }

    private async Task ApplyThemeLiveAsync(string theme)
    {
        var target = ThemeManager.ToElementTheme(theme);
        if (Nav.RequestedTheme == target)
        {
            // 深色↔灰色：先离开当前主题，重写覆盖字典后再回来，强制重新解析。
            Nav.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            ThemePalette.Apply(theme);
            await Task.Delay(60);
        }
        else
        {
            ThemePalette.Apply(theme);
        }

        Nav.RequestedTheme = target;
        ApplyTitleBarTheme(theme);
        SearchBox.RequestedTheme = target;
        ThemeManager.CurrentTheme = theme;
    }

    private void ShowGallery(bool favorites = false, bool recent = false)
    {
        galleryViewModel ??= App.Services.GetRequiredService<GalleryViewModel>();
        if (!galleryWired)
        {
            galleryWired = true;
            galleryViewModel.PropertyChanged += OnGalleryPropertyChanged;
        }
        if (recent)
        {
            galleryViewModel.EnterRecentMode();
        }
        else
        {
            galleryViewModel.ExitRecentMode();
            if (favorites)
            {
                galleryViewModel.EnterFavoritesMode();
            }
            else
            {
                galleryViewModel.ExitFavoritesMode();
            }
        }

        galleryPage ??= new GalleryPage(galleryViewModel);
        galleryPage.ActivateView();
        ContentFrame.Content = galleryPage;
        SelectNavItem(recent ? "recent" : favorites ? "favorites" : "gallery");
    }

    private void ShowInitialization()
    {
        initializationViewModel ??= App.Services.GetRequiredService<InitializationViewModel>();
        initializationViewModel.Succeeded -= OnInitializationSucceeded;
        initializationViewModel.Succeeded += OnInitializationSucceeded;
        initializationPage ??= new InitializationPage(initializationViewModel);
        ContentFrame.Content = initializationPage;
        SelectNavItem("gallery");
    }

    private void OnInitializationSucceeded()
    {
        DispatcherQueue.TryEnqueue(() => ShowGallery());
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        switch (args.InvokedItemContainer?.Tag)
        {
            case "gallery":
                ShowGallery();
                break;
            case "favorites":
                ShowGallery(favorites: true);
                break;
            case "recent":
                ShowGallery(recent: true);
                break;
            case "stats":
                ShowStats();
                break;
            case "settings":
                ShowSettings();
                break;
            case "sync":
                ShowSyncCenter();
                break;
        }
    }

    private void ShowStats()
    {
        statisticsViewModel ??= App.Services.GetRequiredService<StatisticsViewModel>();
        statisticsPage ??= new Views.StatisticsPage(statisticsViewModel);
        statisticsPage.ActivateView();
        ContentFrame.Content = statisticsPage;
        SelectNavItem("stats");
    }

    private void ShowSyncCenter()
    {
        syncCenterViewModel ??= App.Services.GetRequiredService<SyncCenterViewModel>();
        syncCenterPage ??= new Views.SyncCenterPage(syncCenterViewModel);
        syncCenterPage.ActivateView();
        ContentFrame.Content = syncCenterPage;
        SelectNavItem("sync");
    }

    private void ShowSettings()
    {
        settingsViewModel ??= App.Services.GetRequiredService<SettingsViewModel>();
        settingsPage ??= new Views.SettingsPage(
            settingsViewModel,
            App.Services.GetRequiredService<IBackupService>(),
            App.Services.GetRequiredService<IThumbnailService>());
        ContentFrame.Content = settingsPage;
        SelectNavItem("settings");
    }

    private void SelectNavItem(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if ((string?)item.Tag == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }
}
