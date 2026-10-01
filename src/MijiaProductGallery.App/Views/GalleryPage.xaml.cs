using Microsoft.Extensions.DependencyInjection;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.App.Controls;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>
/// 图库页：ItemsRepeater + UniformGridLayout 虚拟化网格；
/// 卡片交互（拖拽/剪贴板/计数/菜单）由 ProductCardControl 承载，本页负责接线与提示。
/// </summary>
public sealed partial class GalleryPage : Page
{
    private GalleryViewModel? vm;
    private readonly IUsageService usage;
    private ContentDialog? collectionManagerDialog;
    private readonly List<ProductCardControl> liveCards = [];

    public GalleryViewModel Vm => vm ?? throw new InvalidOperationException("图库视图模型尚未初始化");

    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = viewModel;
        usage = App.Services.GetRequiredService<IUsageService>();
        Loaded += OnLoaded;
        CardsRepeater.ElementPrepared += OnElementPrepared;
        CardsRepeater.ElementClearing += OnElementClearing;
        vm.PropertyChanged += OnGalleryPropertyChanged;
        vm.CollectionsReloaded += OnCollectionsReloaded;
        // 页面级接管翻页键（含已被子元素标记处理的按键）：翻页后焦点常落入卡片滚动区，
        // 原生 ScrollView 会消费 PageUp/PageDown，普通冒泡路由第二次起收不到。
        AddHandler(KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);
        // 翻页后把焦点收回页面自身（见 OnPageKeyDown），保证下一枚翻页键仍路由到本页。
        IsTabStop = true;
    }

    /// <summary>收藏夹列表重建后同步选择框显示（ComboBox 对异步重建后的源推送会丢失）。</summary>
    private void OnCollectionsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (vm is not null)
            {
                CollectionBox.SelectedItem = vm.SelectedCollection;
                UpdateQuickRemoveLabels();
            }
        });
    }

    /// <summary>翻页后滚动位置回到顶部；视图/合集变化时刷新快速移除项。</summary>
    private void OnGalleryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MijiaProductGallery.ViewModels.GalleryViewModel.CurrentPage))
        {
            DispatcherQueue.TryEnqueue(() => CardsScroll.ChangeView(null, 0, null, disableAnimation: false));
        }
        else if (e.PropertyName is nameof(MijiaProductGallery.ViewModels.GalleryViewModel.Mode)
            or nameof(MijiaProductGallery.ViewModels.GalleryViewModel.SelectedCollection))
        {
            UpdateQuickRemoveLabels();
        }
    }

    /// <summary>导航回图库时刷新数据（首次导入完成等场景）。</summary>
    public void ActivateView()
    {
        if (vm is null)
        {
            return;
        }

        if (vm.State is GalleryLoadState.Empty or GalleryLoadState.Error)
        {
            _ = vm.LoadAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        if (vm.State == GalleryLoadState.Loading && vm.Cards.Count == 0)
        {
            await vm.LoadAsync();
        }

        SyncFilterCombos();
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        // 卡片模板根即 ProductCardControl（x:Bind 直接实例化，无 ContentPresenter 包装）。
        if (args.Element is ProductCardControl control)
        {
            WireCard(control);
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is ProductCardControl control)
        {
            liveCards.Remove(control);
        }
    }

    private void WireCard(ProductCardControl control)
    {
        // ItemsRepeater 会复用已接线实例（InteractionWired=true），
        // 复用时仍需登记 liveCards 并刷新快速移除标签。
        if (!control.InteractionWired)
        {
            control.InteractionWired = true;
            control.DetailRequested += OnCardDetailRequested;
            control.DragCompleted += OnCardDragCompleted;
            control.ActionFailed += OnCardActionFailed;
            control.ActionInfo += OnCardActionInfo;
            control.QuickRemoveRequested += OnCardQuickRemove;
            control.CreateCollectionRequested += OnCardCreateCollectionRequested;
            control.NotDraggableRequested += OnCardNotDraggable;
        }

        if (!liveCards.Contains(control))
        {
            liveCards.Add(control);
        }

        control.SetQuickRemove(CurrentQuickRemoveLabel());
    }

    /// <summary>当前视图对应的快速移除项文案（Normal 视图隐藏；默认收藏=取消收藏；合集=从合集移除）。</summary>
    private string? CurrentQuickRemoveLabel()
    {
        if (vm is null || !vm.IsFavoritesMode)
        {
            return null;
        }

        return vm.CurrentCollectionName is { } name ? $"从「{name}」移除" : "取消收藏";
    }

    /// <summary>视图/合集变化时刷新全部已实例化卡片的快速移除项
    /// （根层菜单在 Opening 时布局已冻结，不能在 Opening 中增删项）。</summary>
    private void UpdateQuickRemoveLabels()
    {
        var label = CurrentQuickRemoveLabel();
        foreach (var control in liveCards)
        {
            control.SetQuickRemove(label);
        }
    }

    /// <summary>左键单击：ViewCount+1，并打开详情对话框。</summary>
    private async void OnCardDetailRequested(object? sender, ProductCard card)
    {
        try
        {
            await usage.RecordAsync(card.ProductId, UsageType.View, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch
        {
            // 查看计数失败不影响详情展示。
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Title = "产品详情",
            CloseButtonText = "关闭",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = card.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = $"型号：{card.Model}" },
                    new TextBlock { Text = $"品牌：{card.Brand}" },
                    new TextBlock { Text = $"分类：{card.Category}" },
                    new TextBlock
                    {
                        Text = "型号、品牌、分类信息如上，图片可拖拽或复制使用。",
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                },
            },
        };
        _ = await dialog.ShowAsync();
    }

    private void OnCardDragCompleted(object? sender, ProductCard card)
    {
        _ = usage.RecordAsync(card.ProductId, UsageType.Drag, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private void OnCardActionFailed(object? sender, string message)
    {
        ShowNotice(message, InfoBarSeverity.Error);
    }

    private void OnCardActionInfo(object? sender, string message)
    {
        ShowNotice(message);
    }

    private void OnCardNotDraggable(object? sender, ProductCard card)
    {
        ShowNotice($"「{card.Name}」无图片，不能拖拽");
    }

    /// <summary>管理收藏夹：ContentDialog 内嵌 CollectionManagerView（列表/重命名/新建）；删除确认由本页协调。</summary>
    private async void OnManageCollectionsClick(object sender, RoutedEventArgs e)
    {
        await ShowCollectionManagerAsync();
    }

    private async Task ShowCollectionManagerAsync()
    {
        var view = new CollectionManagerView(Vm);
        view.DeleteRequested += OnCollectionDeleteRequested;
        var dialog = new ContentDialog
        {
            Title = "管理收藏夹",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Content = view,
            CloseButtonText = "完成",
            DefaultButton = ContentDialogButton.Close,
        };
        collectionManagerDialog = dialog;
        _ = await dialog.ShowAsync();
        collectionManagerDialog = null;
    }

    /// <summary>删除确认：先收起管理框（ContentDialog 同时只能开一个）→ 确认 → 重开管理框。</summary>
    private async void OnCollectionDeleteRequested(object? sender, (int Id, string Name) request)
    {
        collectionManagerDialog?.Hide();
        collectionManagerDialog = null;
        await Task.Delay(150);

        var confirm = new ContentDialog
        {
            Title = "删除收藏夹？",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                Text = $"将删除「{request.Name}」。收藏夹本身会被删除，其中的产品不会被删除，也不会影响其他收藏夹中的产品。",
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        // ContentDialog 同时只能显示一个：管理框已收起，确认结束后重开。
        var result = await confirm.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var (success, error) = await Vm.DeleteCollectionAsync(request.Id);
            if (!success)
            {
                ShowNotice(error ?? "删除失败", InfoBarSeverity.Error);
            }
        }

        await ShowCollectionManagerAsync();
    }

    /// <summary>快速移除：默认收藏视图取消星标；合集视图仅移出该合集（产品与星标收藏不受影响）。</summary>
    private async void OnCardQuickRemove(object? sender, ProductCard card)
    {
        var collectionName = vm?.CurrentCollectionName;
        if (collectionName is { } name)
        {
            var collectionId = vm?.SelectedCollection?.Id ?? 0;
            if (collectionId <= 0)
            {
                return;
            }

            try
            {
                await App.Services.GetRequiredService<ICollectionRepository>()
                    .RemoveItemAsync(collectionId, card.ProductId);
                ShowNotice($"已从「{name}」移除");
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                or InvalidOperationException
                or System.Runtime.InteropServices.COMException)
            {
                ShowNotice($"移除失败：{exception.Message}", InfoBarSeverity.Error);
            }

            return;
        }

        if (vm is not null)
        {
            await vm.ToggleFavoriteAsync(card);
        }
    }

    /// <summary>从卡片菜单新建收藏夹并加入该产品（重名/空名在对话框内提示后可重试）。</summary>
    private async void OnCardCreateCollectionRequested(object? sender, ProductCard card)
    {
        var nameText = string.Empty;
        string? error = null;
        while (true)
        {
            var nameBox = new TextBox
            {
                PlaceholderText = "收藏夹名称",
                MaxLength = 40,
                MinWidth = 240,
                Text = nameText,
            };
            var errorText = new TextBlock
            {
                FontSize = 12,
                Text = error ?? string.Empty,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                Visibility = error is null ? Visibility.Collapsed : Visibility.Visible,
            };
            var dialog = new ContentDialog
            {
                Title = "新建收藏夹",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
                Content = new StackPanel { Spacing = 10, Children = { nameBox, errorText } },
                PrimaryButtonText = "创建",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            nameText = nameBox.Text;
            var (success, createError) = await Vm.CreateCollectionAsync(nameText);
            if (success)
            {
                error = null;
                try
                {
                    var collectionId = Vm.SelectedCollection?.Id ?? 0;
                    if (collectionId > 0)
                    {
                        await App.Services.GetRequiredService<ICollectionRepository>()
                            .AddItemAsync(collectionId, card.ProductId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                        ShowNotice($"已创建并加入收藏夹「{Vm.SelectedCollection?.Name}」");
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException
                    or InvalidOperationException
                    or System.Runtime.InteropServices.COMException)
                {
                    ShowNotice($"加入收藏夹失败：{exception.Message}", InfoBarSeverity.Error);
                }

                return;
            }

            error = createError;
        }
    }

    /// <summary>导出收藏夹：命名方式 → 保存位置 → 流式 ZIP（进度可取消）。</summary>
    private async void OnExportCollectionClick(object sender, RoutedEventArgs e)
    {
        if (vm is null || !vm.IsFavoritesMode)
        {
            return;
        }

        var items = await vm.GetExportItemsAsync();
        if (items.Count == 0)
        {
            ShowNotice("当前收藏夹没有可导出的产品");
            return;
        }

        var nameByModel = await ShowExportNamingDialogAsync();
        if (nameByModel is null)
        {
            return;
        }

        var file = await PickExportTargetAsync();
        if (file is null)
        {
            return;
        }

        var result = await RunExportAsync(items, file.Path, nameByModel.Value);
        if (result.Cancelled)
        {
            ShowNotice("导出已取消");
            return;
        }

        await ShowExportResultAsync(result);
    }

    /// <summary>命名方式选择：true=Model；false=设备名称；null=取消。</summary>
    private async Task<bool?> ShowExportNamingDialogAsync()
    {
        var byModel = new RadioButton { Content = "使用产品 ID / Model（如 xiaomi.airp.mp5b.png）", Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 2) };
        var byName = new RadioButton
        {
            Content = "使用设备名称（如 小米空气净化器.png）",
            IsChecked = true,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 2),
        };
        var dialog = new ContentDialog
        {
            Title = "导出收藏夹",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "文件命名方式", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    byName,
                    byModel,
                },
            },
            PrimaryButtonText = "选择保存位置",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        var choice = await dialog.ShowAsync();
        if (choice != ContentDialogResult.Primary)
        {
            return null;
        }

        return byModel.IsChecked == true;
    }

    private async Task<Windows.Storage.StorageFile?> PickExportTargetAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"收藏夹导出-{DateTime.Now:yyyyMMdd-HHmm}",
        };
        picker.FileTypeChoices.Add("ZIP 压缩包", new List<string> { ".zip" });
        if (App.MainWindow is not null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        }

        return await picker.PickSaveFileAsync();
    }

    /// <summary>执行导出并驱动进度对话框；返回服务结果。</summary>
    private async Task<CollectionExportResult> RunExportAsync(
        IReadOnlyList<CollectionExportItem> items, string zipPath, bool nameByModel)
    {
        var progressBar = new Microsoft.UI.Xaml.Controls.ProgressBar
        {
            Minimum = 0,
            Maximum = items.Count,
            Width = 320,
        };
        var statusText = new TextBlock
        {
            Text = $"0 / {items.Count}",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12.5,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var dialog = new ContentDialog
        {
            Title = "正在导出收藏夹",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Content = new StackPanel { Spacing = 10, Children = { progressBar, statusText } },
            CloseButtonText = "取消",
        };

        var cts = new CancellationTokenSource();
        dialog.Closed += (_, _) => cts.Cancel();
        _ = dialog.ShowAsync();

        var progress = new Progress<CollectionExportProgress>(p =>
        {
            progressBar.Value = p.Completed;
            statusText.Text = $"{p.Completed} / {p.Total}";
        });

        var service = App.Services.GetRequiredService<ICollectionExportService>();
        var result = await service.ExportAsync(
            new CollectionExportRequest { Items = items, ZipFilePath = zipPath, NameByModel = nameByModel },
            progress,
            cts.Token);
        dialog.Hide();
        return result;
    }

    /// <summary>导出结果提示；存在失败项时提供失败明细查看。</summary>
    private async Task ShowExportResultAsync(CollectionExportResult result)
    {
        var summary = result.FailedItems.Count == 0
            ? $"已导出 {result.ExportedCount} 张图片"
            : $"已导出 {result.ExportedCount} 张，{result.FailedItems.Count} 张失败";
        var dialog = new ContentDialog
        {
            Title = "导出完成",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 },
            CloseButtonText = "完成",
        };
        if (result.FailedItems.Count > 0)
        {
            dialog.PrimaryButtonText = "查看失败列表";
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary)
            {
                var list = new TextBox
                {
                    Text = string.Join(Environment.NewLine, result.FailedItems),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinWidth = 380,
                    MinHeight = 200,
                    AcceptsReturn = true,
                };
                var failedDialog = new ContentDialog
                {
                    Title = "导出失败明细",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
                    Content = new ScrollViewer { Content = list, MaxHeight = 380 },
                    CloseButtonText = "关闭",
                };
                _ = await failedDialog.ShowAsync();
            }
        }
        else
        {
            _ = await dialog.ShowAsync();
        }
    }

    /// <summary>收藏视图空状态：跳回全部产品图库。</summary>
    private void OnBrowseGalleryClick(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.ShowGallery();
    }

    /// <summary>收藏夹下拉交互选择（SelectedItem 为 OneWay 绑定，避免列表重建时写回 null）。</summary>
    private void OnCollectionBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null
            || sender is not ComboBox { SelectedItem: CollectionOption option }
            || ReferenceEquals(option, vm.SelectedCollection))
        {
            return;
        }

        vm.SelectedCollection = option;
    }

    private void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = severity == InfoBarSeverity.Error ? "操作失败" : "提示";
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
        if (severity == InfoBarSeverity.Error)
        {
            // 错误常驻，由用户关闭，避免错过关键信息。
            return;
        }

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (NoticeBar.Message == message)
            {
                NoticeBar.IsOpen = false;
            }
        };
        timer.Start();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        await vm.LoadAsync();
    }

    private void OnSortItemClick(object sender, RoutedEventArgs e)
    {
        if (vm is null || sender is not MenuFlyoutItem { Tag: string tag })
        {
            return;
        }

        if (tag == "default")
        {
            vm.ApplySort(null);
            return;
        }

        var parts = tag.Split(':');
        if (parts.Length == 2
            && Enum.TryParse<ProductSortField>(parts[0], ignoreCase: true, out var field)
            && Enum.TryParse<SortDirection>(parts[1], ignoreCase: true, out var direction))
        {
            vm.ApplySort(new ProductSort { Field = field, Direction = direction });
        }
    }

    private void OnRandomEnterClick(object sender, RoutedEventArgs e)
    {
        vm?.EnterRandomMode();
    }

    private void OnRandomRefreshClick(object sender, RoutedEventArgs e)
    {
        vm?.RefreshRandom();
    }

    private void OnRandomExitClick(object sender, RoutedEventArgs e)
    {
        vm?.ExitRandomMode();
    }

    private void OnFilterToggleClick(object sender, RoutedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        SyncFilterCombos();
        vm.FilterPane.IsOpen = !vm.FilterPane.IsOpen;
        if (vm.FilterPane.IsOpen)
        {
            // 打开后焦点移入面板，Tab 顺序从面板内开始；淡入过渡。
            FilterPaneBorder.Opacity = 0;
            FilterPaneBorder.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            PlayPaneFadeIn();
        }
        else
        {
            FilterButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }
    }

    /// <summary>筛选面板原生淡入（150ms，一次性，不阻塞交互）。</summary>
    private void PlayPaneFadeIn()
    {
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(150)),
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, FilterPaneBorder);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        vm.ClearAllFilters();
        suppressComboEvents = true;
        AvailabilityCombo.SelectedIndex = 0;
        ImageCombo.SelectedIndex = 0;
        UsageCombo.SelectedIndex = 0;
        DateCombo.SelectedIndex = 0;
        suppressComboEvents = false;
    }

    /// <summary>把筛选面板四个下拉的显示状态同步为视图模型当前值（防抖期间不回写）。</summary>
    private bool suppressComboEvents;

    private void SyncFilterCombos()
    {
        if (vm is null)
        {
            return;
        }

        suppressComboEvents = true;
        AvailabilityCombo.SelectedIndex = vm.FilterPane.Availability switch
        {
            AvailabilityOption.AvailableOnly => 1,
            AvailabilityOption.DelistedOnly => 2,
            _ => 0,
        };
        ImageCombo.SelectedIndex = vm.FilterPane.ImageOptionValue switch
        {
            ImageOption.WithImage => 1,
            ImageOption.WithoutImage => 2,
            _ => 0,
        };
        UsageCombo.SelectedIndex = vm.FilterPane.Usage switch
        {
            UsageRange.NeverUsed => 1,
            UsageRange.Used => 2,
            UsageRange.HighUsage => 3,
            _ => 0,
        };
        DateCombo.SelectedIndex = vm.FilterPane.UpdateRange switch
        {
            DateRange.Last7Days => 1,
            DateRange.Last30Days => 2,
            _ => 0,
        };
        suppressComboEvents = false;
    }

    private void OnChipRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: string chipId })
        {
            vm?.RemoveChip(chipId);
        }
    }

    /// <summary>弹窗不在根元素子树内，打开时显式对齐当前主题。</summary>
    private void OnFlyoutOpening(object? sender, object e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase flyout)
        {
            ThemeManager.ApplyToFlyout(flyout);
        }
    }

    private void OnComboOpening(object? sender, object e)
    {
        if (sender is ComboBox combo)
        {
            combo.RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme);
        }
    }

    /// <summary>页码框 Enter：提交界面实际文本，走统一分页管线跳转并全选便于连续输入；
    /// e.Handled 阻断冒泡，避免触发详情或其他快捷键。</summary>
    private void OnPageBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            vm?.SubmitPageText(PageBox.Text);
            PageBox.SelectAll();
        }
    }

    /// <summary>页码框失焦：非法/越界输入校正回当前页。</summary>
    private void OnPageBoxLostFocus(object sender, RoutedEventArgs e)
    {
        vm?.SubmitPageText(PageBox.Text);
    }

    private void OnFirstPageClick(object sender, RoutedEventArgs e) => vm?.GoToFirstPage();

    private void OnPrevPageClick(object sender, RoutedEventArgs e) => vm?.GoToPrevPage();

    private void OnNextPageClick(object sender, RoutedEventArgs e) => vm?.GoToNextPage();

    private void OnLastPageClick(object sender, RoutedEventArgs e) => vm?.GoToLastPage();

    /// <summary>
    /// 分页键盘（页面级监听，handledEventsToo——即使子元素已处理也接管）：
    /// Esc——关闭筛选面板；←/→/PageUp/PageDown——上一页/下一页；
    /// Home/End——第一页/最后一页。
    /// 弹层（菜单/对话框/下拉）与文本框内按键交还原生；无上/下页时不产生动作。
    /// </summary>
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        if (focused is not null && IsInPopup(focused))
        {
            // 菜单/对话框/下拉内的方向键与 Esc 属于弹层自身导航，交还原生。
            return;
        }

        if (focused is TextBox or AutoSuggestBox or ComboBox or ComboBoxItem)
        {
            // 文本编辑与下拉选择的原生键盘行为不受翻页接管影响。
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape && vm.FilterPane.IsOpen)
        {
            vm.FilterPane.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (vm.IsPagedMode && !vm.IsRandomMode)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Left when vm.CanGoPrevPage:
                case Windows.System.VirtualKey.PageUp when vm.CanGoPrevPage:
                    vm.GoToPrevPage();
                    e.Handled = true;
                    RefocusPageForNextKey();
                    return;
                case Windows.System.VirtualKey.Right when vm.CanGoNextPage:
                case Windows.System.VirtualKey.PageDown when vm.CanGoNextPage:
                    vm.GoToNextPage();
                    e.Handled = true;
                    RefocusPageForNextKey();
                    return;
                case Windows.System.VirtualKey.Home when vm.CanGoFirstPage:
                    vm.GoToFirstPage();
                    e.Handled = true;
                    RefocusPageForNextKey();
                    return;
                case Windows.System.VirtualKey.End when vm.CanGoLastPage:
                    vm.GoToLastPage();
                    e.Handled = true;
                    RefocusPageForNextKey();
                    return;
            }
        }
    }

    /// <summary>
    /// 翻页后把键盘焦点收回到页面根：翻页会重建卡片并折叠/恢复分页栏，原焦点元素可能被卸载，
    /// 焦点会落到卡片滚动区等原生消费翻页键的元素上；收回页面自身后，下一枚翻页键必然路由到本页。
    /// </summary>
    private void RefocusPageForNextKey()
    {
        DispatcherQueue.TryEnqueue(() => Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
    }

    /// <summary>判断元素是否位于弹层（Popup）子树内：菜单、对话框、下拉都宿主在弹层中。</summary>
    private static bool IsInPopup(object? element)
    {
        DependencyObject? current = element as DependencyObject;
        while (current is not null)
        {
            if (current is Microsoft.UI.Xaml.Controls.Primitives.Popup)
            {
                return true;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>判断元素是否位于指定祖先的子树内（卡片网格内的方向键交给原生焦点导航）。</summary>
    private static bool IsDescendantOf(Microsoft.UI.Xaml.DependencyObject? element, Microsoft.UI.Xaml.DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (element == ancestor)
            {
                return true;
            }

            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    /// <summary>Esc：筛选面板打开时优先关闭面板，其余场景交给原生处理。</summary>
    private void OnEscapeInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (vm is { FilterPane.IsOpen: true })
        {
            vm.FilterPane.IsOpen = false;
            args.Handled = true;
        }
    }

    private void OnBrandSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (vm is null)
        {
            return;
        }

        vm.FilterPane.BrandSearchText = BrandSearchBox.Text;
    }

    private void OnAvailabilitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null || suppressComboEvents)
        {
            return;
        }

        vm.FilterPane.Availability = ((ComboBox)sender).SelectedIndex switch
        {
            1 => AvailabilityOption.AvailableOnly,
            2 => AvailabilityOption.DelistedOnly,
            _ => AvailabilityOption.All,
        };
    }

    private void OnImageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null || suppressComboEvents)
        {
            return;
        }

        vm.FilterPane.ImageOptionValue = ((ComboBox)sender).SelectedIndex switch
        {
            1 => ImageOption.WithImage,
            2 => ImageOption.WithoutImage,
            _ => ImageOption.All,
        };
    }

    private void OnUsageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null || suppressComboEvents)
        {
            return;
        }

        vm.FilterPane.Usage = ((ComboBox)sender).SelectedIndex switch
        {
            1 => UsageRange.NeverUsed,
            2 => UsageRange.Used,
            3 => UsageRange.HighUsage,
            _ => UsageRange.None,
        };
    }

    private void OnDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null || suppressComboEvents)
        {
            return;
        }

        vm.FilterPane.UpdateRange = ((ComboBox)sender).SelectedIndex switch
        {
            1 => DateRange.Last7Days,
            2 => DateRange.Last30Days,
            _ => DateRange.All,
        };
    }
}
