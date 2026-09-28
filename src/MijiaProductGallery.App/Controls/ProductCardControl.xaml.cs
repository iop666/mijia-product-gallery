using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
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
using Windows.Storage.Streams;

namespace MijiaProductGallery.App.Controls;

/// <summary>
/// 产品卡片控件：Hover/Pressed/DragCue 视觉态、原生拖拽（长按或位移阈值触发）、
/// 右键菜单（复制图片/名称/型号/品牌/完整信息/加入收藏）、悬停快速复制按钮。
/// 无图型号禁止拖拽与复制图片，绝不生成空文件。
/// </summary>
public sealed partial class ProductCardControl : UserControl
{
    /// <summary>左键单击（打开详情入口）。</summary>
    public event EventHandler<ProductCard>? DetailRequested;

    /// <summary>拖拽已被外部接收（Explorer/图像软件），应当 DragCount+1。</summary>
    public event EventHandler<ProductCard>? DragCompleted;

    /// <summary>右键"加入收藏"请求（收藏系统在 Phase 12 实现）。</summary>
    public event EventHandler<ProductCard>? FavoriteRequested;

    /// <summary>动作失败（需要 UI 提示错误状态）。</summary>
    public event EventHandler<string>? ActionFailed;

    /// <summary>尝试拖拽无图型号（需要 UI 提示不可拖拽状态）。</summary>
    public event EventHandler<ProductCard>? NotDraggableRequested;

    /// <summary>相对路径 → 绝对路径解析器（由页面注入）。</summary>
    public Func<ProductCard, string?>? PathResolver { get; set; }

    /// <summary>事件是否已由页面接线（虚拟化复用防重复订阅）。</summary>
    public bool InteractionWired { get; set; }

    private ProductCard? card;

    private bool isPointerPressed;
    private bool dragStarted;
    private bool dragArmed;
    private Windows.Foundation.Point pressedPoint;
    private Microsoft.UI.Input.PointerPoint? dragStartPoint;
    private int pressedAtMilliseconds;

    public ProductCardControl()
    {
        InitializeComponent();
        Loaded += OnControlLoaded;
        RootGrid.PointerPressed += OnPointerPressed;
        RootGrid.PointerMoved += OnPointerMoved;
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
            UpdateMenuStates();
            ResetInteraction();
            RequestThumbnail();
        }
    }

    /// <summary>卡片就绪后请求缩略图（进入视口由 ItemsRepeater 实例化触发）。</summary>
    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        RequestThumbnail();
    }

    private void RequestThumbnail()
    {
        if (Card is not null)
        {
            App.Services.GetRequiredService<ThumbnailLoadQueue>().Request(Card);
        }
    }

    private DispatcherQueueTimer CreateHoldTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(DragGesture.HoldThresholdMilliseconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (isPointerPressed && !dragStarted)
            {
                dragArmed = true;
                VisualStateManager.GoToState(this, "DragCue", useTransitions: false);
            }
        };
        return timer;
    }

    private DispatcherQueueTimer? holdTimer;

    private void EnsureHoldTimer()
    {
        holdTimer ??= CreateHoldTimer();
    }

    private void UpdateMenuStates()
    {
        var hasImage = Card?.HasImage ?? false;
        MenuCopyImage.IsEnabled = hasImage;
        MenuFavorite.Text = Card?.IsFavorite == true ? "★ 取消收藏" : "☆ 加入收藏";
    }

    private void ResetInteraction()
    {
        isPointerPressed = false;
        dragStarted = false;
        dragArmed = false;
        holdTimer?.Stop();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Card is null)
        {
            return;
        }

        isPointerPressed = true;
        dragStarted = false;
        dragArmed = false;
        pressedPoint = e.GetCurrentPoint(this).Position;
        dragStartPoint = e.GetCurrentPoint(this);
        pressedAtMilliseconds = Environment.TickCount;
        RootGrid.CapturePointer(e.Pointer);
        EnsureHoldTimer();
        holdTimer!.Start();
        VisualStateManager.GoToState(this, "Pressed", useTransitions: false);
    }

    private async void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!isPointerPressed || dragStarted || Card is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(this).Position;
        var dx = position.X - pressedPoint.X;
        var dy = position.Y - pressedPoint.Y;
        var shouldStart = (dragArmed && DragGesture.ShouldStartByMove(dx * 4, dy * 4))
            || (!dragArmed && DragGesture.ShouldStartByMove(dx, dy));
        if (!shouldStart)
        {
            return;
        }

        dragStarted = true;
        holdTimer?.Stop();
        await BeginDragAsync(e);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!isPointerPressed)
        {
            return;
        }

        var wasClick = !dragStarted && !dragArmed;
        var position = e.GetCurrentPoint(this).Position;
        var moved = DragGesture.ShouldStartByMove(
            position.X - pressedPoint.X,
            position.Y - pressedPoint.Y);
        wasClick &= !moved;

        isPointerPressed = false;
        dragArmed = false;
        holdTimer?.Stop();
        VisualStateManager.GoToState(this, "Normal", useTransitions: false);

        if (wasClick && Card is not null)
        {
            DetailRequested?.Invoke(this, Card);
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!isPointerPressed)
        {
            VisualStateManager.GoToState(this, "PointerOver", useTransitions: false);
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!isPointerPressed)
        {
            VisualStateManager.GoToState(this, "Normal", useTransitions: false);
        }
    }

    private async Task BeginDragAsync(PointerRoutedEventArgs e)
    {
        var current = Card;
        if (current is null)
        {
            return;
        }

        if (!DragGesture.CanDrag(current))
        {
            dragStarted = false;
            NotDraggableRequested?.Invoke(this, current);
            return;
        }

        var absolutePath = PathResolver?.Invoke(current);
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            dragStarted = false;
            ActionFailed?.Invoke(this, "图片文件缺失，请先完成同步后再试");
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy,
            };
            package.SetStorageItems(new[] { file });
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            package.Properties.Title = current.Name;

            var result = dragStartPoint is not null
                ? await StartDragAsync(dragStartPoint)
                : DataPackageOperation.None;
            ResetInteraction();
            VisualStateManager.GoToState(this, "Normal", useTransitions: false);
            if (result.HasFlag(DataPackageOperation.Copy))
            {
                DragCompleted?.Invoke(this, current);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException or FileNotFoundException)
        {
            dragStarted = false;
            ActionFailed?.Invoke(this, $"拖拽启动失败：{exception.Message}");
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
