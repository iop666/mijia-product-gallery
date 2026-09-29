using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.App.Views;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace MijiaProductGallery.App.Controls;

/// <summary>
/// 产品卡片控件：Hover/Pressed 视觉态、框架原生拖拽（CanDrag + DragStarting 提供真实图片文件，
/// Explorer/图像软件可接收）、右键菜单（复制图片/名称/型号/品牌/完整信息/加入收藏）、
/// 悬停快速复制按钮。无图型号禁止拖拽与复制图片，绝不生成空文件。
/// </summary>
public sealed partial class ProductCardControl : UserControl
{
    /// <summary>左键单击（打开详情入口）。</summary>
    public event EventHandler<ProductCard>? DetailRequested;

    /// <summary>拖拽已被外部接收（Explorer/图像软件），应当 DragCount+1。</summary>
    public event EventHandler<ProductCard>? DragCompleted;

    /// <summary>右键"加入收藏"请求。</summary>
    public event EventHandler<ProductCard>? FavoriteRequested;

    /// <summary>动作失败（需要 UI 提示错误状态）。</summary>
    public event EventHandler<string>? ActionFailed;

    /// <summary>尝试拖拽无图型号（需要 UI 提示不可拖拽状态）。</summary>
    public event EventHandler<ProductCard>? NotDraggableRequested;

    /// <summary>事件是否已由页面接线（虚拟化复用防重复订阅）。</summary>
    public bool InteractionWired { get; set; }

    private ProductCard? card;

    private Windows.Foundation.Point pressedPoint;

    public ProductCardControl()
    {
        InitializeComponent();
        RootGrid.CanDrag = true;
        RootGrid.IsTabStop = true;
        RootGrid.UseSystemFocusVisuals = true;
        RootGrid.KeyDown += OnRootKeyDown;
        RootGrid.DragStarting += OnRootDragStarting;
        RootGrid.DropCompleted += OnRootDropCompleted;
        RootGrid.PointerPressed += OnPointerPressed;
        RootGrid.PointerReleased += OnPointerReleased;
        RootGrid.PointerEntered += OnPointerEntered;
        RootGrid.PointerExited += OnPointerExited;
    }

    public ProductCard? Card
    {
        get => card;
        set
        {
            card = value;
            DataContext = value;
            // UI Automation 语义：读屏与键盘用户可识别卡片指向的产品。
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                RootGrid, value is null ? string.Empty : $"{value.Name}（{value.Model}）");
            UpdateMenuStates();
            RequestThumbnail();
        }
    }

    /// <summary>键盘操作：Enter/Space 打开详情（与左键单击一致）。</summary>
    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (Card is null)
        {
            return;
        }

        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            e.Handled = true;
            DetailRequested?.Invoke(this, Card);
        }
    }

    private void RequestThumbnail()
    {
        if (Card is not null)
        {
            App.Services.GetRequiredService<ThumbnailLoadQueue>().Request(Card);
        }
    }

    private void UpdateMenuStates()
    {
        var hasImage = Card?.HasImage ?? false;
        MenuCopyImage.IsEnabled = hasImage;
        MenuFavorite.Text = Card?.IsFavorite == true ? "★ 取消收藏" : "☆ 加入收藏";
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        pressedPoint = e.GetCurrentPoint(this).Position;
        VisualStateManager.GoToState(this, "Pressed", useTransitions: false);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "Normal", useTransitions: false);

        var position = e.GetCurrentPoint(this).Position;
        var moved = DragGesture.ShouldStartByMove(
            position.X - pressedPoint.X,
            position.Y - pressedPoint.Y);
        if (!moved && Card is not null)
        {
            DetailRequested?.Invoke(this, Card);
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "PointerOver", useTransitions: false);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "Normal", useTransitions: false);
    }

    /// <summary>
    /// 框架手势触发拖拽：经 Deferral 异步解析原图文件并放入 DataPackage（StorageItems），
    /// 无图或文件缺失时取消并提示。
    /// </summary>
    private async void OnRootDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var current = Card;
            if (current is null || !DragGesture.CanDrag(current))
            {
                args.Cancel = true;
                if (current is not null)
                {
                    NotDraggableRequested?.Invoke(this, current);
                }

                return;
            }

            var absolutePath = App.Services
                .GetRequiredService<IImageStore>()
                .ResolveAbsolutePath(current.ImagePath);
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                args.Cancel = true;
                ActionFailed?.Invoke(this, "图片文件缺失，请先完成同步后再试");
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.Data.SetStorageItems(new[] { file });
            args.Data.Properties.Title = current.Name;
            args.AllowedOperations = DataPackageOperation.Copy;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException or FileNotFoundException)
        {
            args.Cancel = true;
            ActionFailed?.Invoke(this, $"拖拽启动失败：{exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnRootDropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        if (args.DropResult.HasFlag(DataPackageOperation.Copy) && Card is not null)
        {
            DragCompleted?.Invoke(this, Card);
        }
    }

    private async void OnHoverCopyClick(object sender, RoutedEventArgs e)
    {
        await CopyImageAsync();
    }

    private async void OnMenuCopyImageClick(object sender, RoutedEventArgs e)
    {
        await CopyImageAsync();
    }

    private async Task CopyImageAsync()
    {
        if (Card is null)
        {
            return;
        }

        var result = await App.Services.GetRequiredService<CardActionService>().CopyImageAsync(Card);
        if (!result.Success)
        {
            ActionFailed?.Invoke(this, result.ErrorMessage ?? "复制失败");
        }
    }

    private async void CopyText(CardTextKind kind)
    {
        if (Card is null)
        {
            return;
        }

        var result = await App.Services.GetRequiredService<CardActionService>().CopyTextAsync(Card, kind);
        if (!result.Success)
        {
            ActionFailed?.Invoke(this, result.ErrorMessage ?? "复制失败");
        }
    }

    private void OnMenuCopyNameClick(object sender, RoutedEventArgs e)
    {
        CopyText(CardTextKind.Name);
    }

    private void OnMenuCopyModelClick(object sender, RoutedEventArgs e)
    {
        CopyText(CardTextKind.Model);
    }

    private void OnMenuCopyBrandClick(object sender, RoutedEventArgs e)
    {
        CopyText(CardTextKind.Brand);
    }

    private void OnMenuCopyFullInfoClick(object sender, RoutedEventArgs e)
    {
        CopyText(CardTextKind.FullInfo);
    }

    private void OnMenuFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (Card is not null)
        {
            FavoriteRequested?.Invoke(this, Card);
        }
    }
}
