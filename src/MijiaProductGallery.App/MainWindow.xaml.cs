using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.App.Services;
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
        // 内容延伸进标题栏：客户区全部由应用底色铺满，接缝不透窗口底色；
        // 拖拽区为顶部标题条，右上系统按钮浮于其上。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        ApplyTitleBarTheme(App.InitialTheme);
        ApplyRootBackground(App.InitialTheme);
        App.Services.GetRequiredService<SettingsViewModel>().ThemeChanged += OnThemeChanged;
        Nav.Loaded += OnLoaded;
        Nav.SizeChanged += OnNavSizeChanged;
        Nav.KeyDown += OnNavKeyDown;
    }

    /// <summary>导航根部铺不透明的主题底色：窗口级背景资源跟随启动主题，
    /// 不透明底色使接缝处不再透出启动时的深色/浅色窗口底。</summary>
    private void ApplyRootBackground(string theme)
    {
        var argb = theme switch
        {
            "Dark" => 0xFF1C1C1C,
            "Gray" => 0xFF3B3B3B,
            _ => 0xFFFFFFFF,
        };
        WindowGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
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

    /// <summary>内容延伸进标题栏后，系统按钮浮于应用底色上：仅设置前景与悬停色，
    /// 按钮底透明；标题文字/图标由拖拽条元素承载。</summary>
    private void ApplyTitleBarTheme(string theme)
    {
        var bar = AppWindow.TitleBar;
        var dark = theme is "Dark" or "Gray";
        bar.ForegroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 240, 240, 240)
            : Windows.UI.Color.FromArgb(255, 26, 26, 26);
        bar.ButtonForegroundColor = bar.ForegroundColor;
        bar.ButtonHoverForegroundColor = dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
        bar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 66, 66, 66)
            : Windows.UI.Color.FromArgb(255, 229, 229, 229);
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
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

    /// <summary>Ctrl+F 聚焦搜索框（KeyDown 路由，避免加速器悬停提示）。</summary>
    private void OnNavKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrlDown && e.Key == Windows.System.VirtualKey.F)
        {
            SearchBox.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
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

        // 设置页修改每页数量/浏览模式后，图库以新参数重新加载第 1 页。
        var settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        var galleryInstance = App.Services.GetRequiredService<GalleryViewModel>();
        settingsViewModel.PageSizeChanged += size => DispatcherQueue.TryEnqueue(() => galleryInstance.OnBrowseSettingsChanged());
        settingsViewModel.BrowseModeChanged += _ => DispatcherQueue.TryEnqueue(() => galleryInstance.OnBrowseSettingsChanged());

        // 首次启动：展示使用条款与免责声明，同意后进入应用。
        var settingsRepository = App.Services.GetRequiredService<ISettingsRepository>();
        var licenseAgreed = await settingsRepository.GetValueAsync(AppSettingsKeys.LicenseAgreed, false);
        if (!licenseAgreed)
        {
            var agreed = await ShowLicenseDialogAsync();
            if (!agreed)
            {
                Application.Current.Exit();
                return;
            }

            await settingsRepository.SetValueAsync(AppSettingsKeys.LicenseAgreed, true);
        }

        var products = App.Services.GetRequiredService<IProductRepository>();
        var hasProducts = await products.CountAsync() > 0;
        if (!hasProducts)
        {
            // 首次初始化（空库）：抓取期间不展示可操作 GUI，仅显示控制台加载窗口；
            // 完成后进入图库，失败或控制台不可用时回退初始化页（重试/选种子包）。
            AppWindow.Hide();
            var result = await FirstRunConsole.RunAsync(
                App.Services.GetRequiredService<IFirstRunInitializer>(),
                App.Services.GetRequiredService<ISyncService>());
            if (result == FirstRunConsoleResult.Succeeded)
            {
                ShowGallery();
            }
            else
            {
                ShowInitialization();
            }
        }
        else
        {
            ShowGallery();
        }

        StartAutoSyncScheduler();

        // 控制台阶段窗口被隐藏：无论走哪条路径都恢复显示并置前。
        AppWindow.Show();
        Activate();
    }

    /// <summary>启动自动同步调度器并执行一次启动检查（"每次启动"频率在此时触发）。</summary>
    private void StartAutoSyncScheduler()
    {
        var scheduler = App.Services.GetRequiredService<IAutoSyncScheduler>();
        scheduler.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await scheduler.CheckAndTriggerAsync(isStartupCheck: true);
            }
            catch
            {
                // 启动检查失败不影响进入应用；等待下一个检查周期。
            }
        });
    }

    /// <summary>主题实时切换：窗口实例保持不变，当前页面与主要 UI 状态全部保留。
    /// 深色↔灰色元素主题相同、无变更信号，借一次浅色过渡强制全树主题资源重解析；
    /// 弹窗/对话框在打开时经 ThemeManager 对齐主题。</summary>
    /// <summary>首次启动使用条款弹窗：声明应用定位、内容版权与禁止商用。</summary>
    private async Task<bool> ShowLicenseDialogAsync()
    {
        var content = new StackPanel { Spacing = 10, MaxWidth = 520 };
        content.Children.Add(new TextBlock
        {
            Text = "米家产品示例图库是一款开源的 Windows 桌面应用，源代码以 MIT 协议提供：",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "github.com/iop666/mijia-product-gallery",
            NavigateUri = new Uri("https://github.com/iop666/mijia-product-gallery"),
        });
        content.Children.Add(new TextBlock
        {
            Text = "本应用仅供个人学习、研究与软件测试使用，严禁商用。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "运行时从米家百科公开产品库获取的产品信息与示例样图，版权归小米公司及相关权利人所有；使用远端内容须遵守相关法律法规及来源方的条款。",
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Nav.XamlRoot,
            Title = "使用条款与免责声明",
            Content = content,
            PrimaryButtonText = "同意并继续",
            CloseButtonText = "不同意并退出",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void OnThemeChanged(string theme)
    {
        DispatcherQueue.TryEnqueue(() => _ = ApplyThemeLiveAsync(theme));
    }

    private async Task ApplyThemeLiveAsync(string theme)
    {
        try
        {
            // 不透明根底色先行：任何后续步骤异常都保证接缝不透窗口底色。
            ApplyRootBackground(theme);
            ApplyTitleBarTheme(theme);
            var target = ThemeManager.ToElementTheme(theme);
            if (Nav.RequestedTheme == target)
            {
                // 深色↔灰色：先离开当前主题，重写覆盖字典后再回来，强制重新解析。
                Nav.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                ThemePalette.Apply(theme);
                await Task.Delay(60);
                Nav.RequestedTheme = target;
            }
            else
            {
                ThemePalette.Apply(theme);
                Nav.RequestedTheme = target;
            }

            SearchBox.RequestedTheme = target;
            ThemeManager.CurrentTheme = theme;
        }
        catch (Exception exception)
        {
            // 主题切换失败回滚到安全状态：仅根底色与元素主题，避免半更新。
            ApplyRootBackground(theme);
            Nav.RequestedTheme = ThemeManager.ToElementTheme(theme);
            LogThemeError(exception);
        }
    }

    /// <summary>主题切换异常落盘（诊断用，不影响正常流程）。</summary>
    private static void LogThemeError(Exception exception)
    {
        try
        {
            var dir = new MijiaProductGallery.Infrastructure.Database.DatabasePaths().LogsDirectory;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "theme-error.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {exception}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    /// <summary>显示全部产品图库（供收藏空状态"浏览图库"等入口调用）。</summary>
    public void ShowGallery(bool favorites = false, bool recent = false)
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
