using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core.Enums;
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
    private readonly CardActionService cardActions;
    private readonly IUsageService usage;

    public GalleryViewModel Vm => vm ?? throw new InvalidOperationException("图库视图模型尚未初始化");

    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        cardActions = App.Services.GetRequiredService<CardActionService>();
        usage = App.Services.GetRequiredService<IUsageService>();
        Loaded += OnLoaded;
        CardsRepeater.ElementPrepared += OnElementPrepared;
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
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not ContentPresenter { Content: ProductCardControl control }
            || control.InteractionWired)
        {
            return;
        }

        control.InteractionWired = true;
        control.PathResolver = card => App.Services.GetRequiredService<IImageStore>().ResolveAbsolutePath(card.ImagePath);
        control.DetailRequested += OnCardDetailRequested;
        control.DragCompleted += OnCardDragCompleted;
        control.FavoriteRequested += OnCardFavoriteRequested;
        control.ActionFailed += OnCardActionFailed;
        control.NotDraggableRequested += OnCardNotDraggable;
    }

    /// <summary>左键单击：ViewCount+1，并打开占位详情（完整详情页在后续阶段实现）。</summary>
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
            Title = "产品详情（占位）",
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
                        Text = "完整详情页将在后续阶段提供。",
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

    private async void OnCardFavoriteRequested(object? sender, ProductCard card)
    {
        // 经 FavoriteService 切换收藏（幂等，用户数据）。
        await cardActions.ToggleFavoriteAsync(card);
    }

    private void OnCardActionFailed(object? sender, string message)
    {
        ShowNotice(message, InfoBarSeverity.Error);
    }

    private void OnCardNotDraggable(object? sender, ProductCard card)
    {
        ShowNotice($"「{card.Name}」无图片，不能拖拽");
    }

    private void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Title = severity == InfoBarSeverity.Error ? "操作失败" : "提示";
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
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

    private void OnSortMenuClick(object sender, RoutedEventArgs e)
    {
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

        vm.FilterPane.IsOpen = !vm.FilterPane.IsOpen;
    }

    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        vm?.ClearAllFilters();
    }

    private void OnChipRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: string chipId })
        {
            vm?.RemoveChip(chipId);
        }
    }

    private void OnAvailabilitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm is null)
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
        if (vm is null)
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
        if (vm is null)
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
        if (vm is null)
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
