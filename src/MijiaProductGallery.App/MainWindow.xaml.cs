using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.App.Views;
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
        ExtendsContentIntoTitleBar = true;
        Nav.Loaded += OnLoaded;
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

    private void ShowGallery(bool favorites = false, bool recent = false)
    {
        galleryViewModel ??= App.Services.GetRequiredService<GalleryViewModel>();
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

        // 最近使用 / 同步中心 / 设置在后续阶段开放。
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
        settingsPage ??= new Views.SettingsPage(settingsViewModel);
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
