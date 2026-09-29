using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.App.Views;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
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

    /// <summary>动作失败（需要 UI 提示错误状态）。</summary>
    public event EventHandler<string>? ActionFailed;

    /// <summary>动作成功提示（需要 UI 提示信息状态）。</summary>
    public event EventHandler<string>? ActionInfo;

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

    private IFavoriteService Favorites => App.Services.GetRequiredService<IFavoriteService>();

    private IUsageService Usage => App.Services.GetRequiredService<IUsageService>();

    private ISystemClipboard Clipboard => App.Services.GetRequiredService<ISystemClipboard>();

    private IImageStore ImageStore => App.Services.GetRequiredService<IImageStore>();

    private ICollectionRepository Collections => App.Services.GetRequiredService<ICollectionRepository>();

    public ProductCard? Card
    {
        get => card;
        set
        {
            card = value;
            DataContext = value;
            UpdateAutomationSemantics();
            UpdateMenuStates();
            RequestThumbnail();
        }
    }

    /// <summary>右键菜单不在根元素子树内，打开时显式对齐当前主题；"添加到收藏夹"按当前数据重建。</summary>
    private void OnMenuFlyoutOpening(object? sender, object e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase flyout)
        {
            ThemeManager.ApplyToFlyout(flyout);
        }

        PopulateCollectionMenu();
    }

    /// <summary>重建"添加到收藏夹"子菜单（异步取列表；已加入项禁用标记）。</summary>
    private void PopulateCollectionMenu()
    {
        MenuAddToCollection.Items.Clear();
        if (Card is null)
        {
            return;
        }

        _ = PopulateCollectionMenuCoreAsync(Card.ProductId);
    }

    private async Task PopulateCollectionMenuCoreAsync(int productId)
    {
        List<Collection> rows;
        try
        {
            rows = [.. await Collections.GetAllAsync()];
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            return;
        }

        if (rows.Count == 0)
        {
            MenuAddToCollection.Items.Add(new MenuFlyoutItem
            {
                Text = "暂无收藏夹（收藏视图可管理）",
                IsEnabled = false,
            });
            return;
        }

        foreach (var row in rows)
        {
            var joined = (await Collections.GetProductIdsAsync(row.Id)).Contains(productId);
            var item = new MenuFlyoutItem
            {
                Text = joined ? $"{row.Name}（已加入）" : row.Name,
                IsEnabled = !joined,
            };
            var collectionId = row.Id;
            item.Click += (_, _) => _ = AddToCollectionAsync(collectionId, row.Name);
            MenuAddToCollection.Items.Add(item);
        }
    }

    private async Task AddToCollectionAsync(int collectionId, string name)
    {
        if (Card is null)
        {
            return;
        }

        try
        {
            await Collections.AddItemAsync(collectionId, Card.ProductId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ActionInfo?.Invoke(this, $"已添加到收藏夹「{name}」");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            ActionFailed?.Invoke(this, $"添加到收藏夹失败：{exception.Message}");
        }
    }

    /// <summary>卡片无障碍语义：名称（名称+型号）+ 类型（产品卡片）。</summary>
    private void UpdateAutomationSemantics()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            RootGrid, card is null ? string.Empty : $"{card.Name}（{card.Model}）");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemType(RootGrid, "产品卡片");
    }

    /// <summary>键盘操作：Enter/Space 打开详情；菜单键/Shift+F10 打开右键菜单（与右键一致）。</summary>
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
            return;
        }

        // 菜单键或 Shift+F10：键盘打开右键菜单（若框架已打开则不重复）。
        var isMenuKey = e.Key == Windows.System.VirtualKey.Menu;
        var isShiftF10 = e.Key == Windows.System.VirtualKey.F10
            && Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if ((isMenuKey || isShiftF10) && !CardContextMenu.IsOpen)
        {
            e.Handled = true;
            CardContextMenu.ShowAt(RootGrid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(20, 20),
            });
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
        // 星形图标已在 XAML 中固定，文字不带星号前缀（避免与图标重复）。
        MenuFavorite.Text = Card?.IsFavorite == true ? "取消收藏" : "加入收藏";
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

    private async void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(this, "Normal", useTransitions: false);

        var position = e.GetCurrentPoint(this).Position;
        var moved = DragGesture.ShouldStartByMove(
            position.X - pressedPoint.X,
            position.Y - pressedPoint.Y);
        if (moved || Card is null)
        {
            return;
        }

        try
        {
            await Usage.RecordAsync(Card.ProductId, UsageType.View, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch
        {
            // 查看计数失败不影响详情展示。
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = ActualTheme,
            Title = Card.Name,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };
        var info = new StackPanel { Spacing = 8 };
        info.Children.Add(new TextBlock
        {
            Text = Card.Model,
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        info.Children.Add(new TextBlock
        {
            Text = $"{Card.Brand} · {Card.Category}",
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });
        if (Card.ImageFileName is not null)
        {
            info.Children.Add(new TextBlock
            {
                Text = $"图片文件：{Card.ImageFileName}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        dialog.Content = info;
        _ = dialog.ShowAsync();
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
        // 拖拽进行中的源卡片高亮（释放后由 DropCompleted 恢复）。
        VisualStateManager.GoToState(this, "DragCue", useTransitions: false);
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
        VisualStateManager.GoToState(this, "Normal", useTransitions: false);
        if (args.DropResult.HasFlag(DataPackageOperation.Copy) && Card is not null)
        {
            _ = Usage.RecordAsync(Card.ProductId, UsageType.Drag, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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

    private async void OnMenuFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (Card is null)
        {
            return;
        }

        try
        {
            Card.IsFavorite = await Favorites.ToggleAsync(Card.ProductId);
            UpdateMenuStates();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            ActionFailed?.Invoke(this, $"收藏切换失败：{exception.Message}");
        }
    }
}
