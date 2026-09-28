using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.Core.Enums;
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
    private readonly GalleryViewModel vm;
    private readonly CardActionService cardActions;
    private readonly IUsageService usage;

    public GalleryViewModel Vm => vm;

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
        if (vm.State is GalleryLoadState.Empty or GalleryLoadState.Error)
        {
            _ = vm.LoadAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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

    private void OnCardFavoriteRequested(object? sender, ProductCard card)
    {
        ShowNotice("收藏功能将在后续阶段开放（当前版本未写入任何数据）");
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
        await vm.LoadAsync();
    }
}
