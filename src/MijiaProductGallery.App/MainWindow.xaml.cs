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

    /// <summary>默认标题栏不跟随应用主题，深色系模式下显式设置标题栏与按钮颜色。</summary>
    private void ApplyTitleBarTheme(string theme)
    {
        if (theme is not ("Dark" or "Gray"))
        {
            return;
        }

        var bar = AppWindow.TitleBar;
        var background = theme == "Gray"
            ? Windows.UI.Color.FromArgb(255, 45, 45, 45)
            : Windows.UI.Color.FromArgb(255, 32, 32, 32);
        var hover = theme == "Gray"
            ? Windows.UI.Color.FromArgb(255, 58, 58, 58)
            : Windows.UI.Color.FromArgb(255, 48, 48, 48);
        bar.ForegroundColor = Windows.UI.Color.FromArgb(255, 240, 240, 240);
        bar.BackgroundColor = background;
        bar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 240, 240, 240);
        bar.ButtonBackgroundColor = background;
        bar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonPressedBackgroundColor = background;
        bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 160, 160, 160);
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
    /// 元素级资源随根元素 RequestedTheme 翻转，窗口/图层背景经画笔覆盖表更新。</summary>
    private void OnThemeChanged(string theme)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Nav.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" or "Gray" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            ThemePalette.Apply(theme);
            ApplyTitleBarTheme(theme);
        });
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
