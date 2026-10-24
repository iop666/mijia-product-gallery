using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>
/// 产品详情弹层：覆盖窗口的轻遮罩 + 居中卡片，点击遮罩空白处、关闭按钮或 Esc 均可退出。
/// 鼠标点击与键盘 Enter 共用此唯一入口，展示缩略图/原图与完整字段并承载卡片操作。
/// </summary>
public static class ProductDetailDialog
{
    private static bool isOpen;

    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        ProductCard card,
        Func<ProductCard, Task<bool>>? toggleFavorite)
    {
        if (isOpen || xamlRoot.Content is not Panel host)
        {
            return;
        }

        isOpen = true;
        try
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var overlay = BuildOverlay(xamlRoot, card, toggleFavorite, host, completed);
            host.Children.Add(overlay);
            overlay.Loaded += (_, _) => overlay.Focus(FocusState.Programmatic);
            await completed.Task;
        }
        finally
        {
            isOpen = false;
        }
    }

    private static Grid BuildOverlay(
        XamlRoot xamlRoot,
        ProductCard card,
        Func<ProductCard, Task<bool>>? toggleFavorite,
        Panel host,
        TaskCompletionSource<bool> completed)
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)),
            RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme),
            IsTabStop = true,
        };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, host is Grid grid ? Math.Max(1, grid.RowDefinitions.Count) : 1);
        Grid.SetColumn(overlay, 0);
        Grid.SetColumnSpan(overlay, host is Grid gridColumns ? Math.Max(1, gridColumns.ColumnDefinitions.Count) : 1);
        Canvas.SetZIndex(overlay, 1000);

        var cardPanel = new Border
        {
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            MinWidth = 660,
            MaxWidth = 760,
            MaxHeight = Math.Max(320, xamlRoot.Size.Height - 96),
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var closeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(closeButton, "关闭产品详情");
        ToolTipService.SetToolTip(closeButton, "关闭（Esc）");

        var header = new Grid { Padding = new Thickness(20, 16, 12, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "产品详情",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        layout.Children.Add(header);

        var body = BuildBody(card, toggleFavorite);
        var scroll = new ScrollViewer
        {
            Content = body,
            Padding = new Thickness(20, 0, 20, 20),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        cardPanel.Child = layout;
        overlay.Children.Add(cardPanel);

        void Close()
        {
            if (host.Children.Remove(overlay))
            {
                completed.TrySetResult(true);
            }
        }

        closeButton.Click += (_, _) => Close();
        overlay.Tapped += (_, args) =>
        {
            // 只有点在遮罩本身（而非卡片）才关闭。
            if (ReferenceEquals(args.OriginalSource, overlay))
            {
                Close();
            }
        };
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape)
            {
                args.Handled = true;
                Close();
            }
        };

        return overlay;
    }

    private static Grid BuildBody(
        ProductCard card,
        Func<ProductCard, Task<bool>>? toggleFavorite)
    {
        var body = new Grid { ColumnSpacing = 18 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var imageHost = new Grid
        {
            Width = 240,
            Height = 240,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
        };
        var imageBytesWarning = false;
        var absolutePath = App.Services.GetRequiredService<IImageStore>()
            .ResolveAbsolutePath(card.ImagePath);
        if (!string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath))
        {
            imageHost.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(absolutePath))
                {
                    DecodePixelType = DecodePixelType.Logical,
                    DecodePixelWidth = 600,
                },
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8),
            });
        }
        else
        {
            imageBytesWarning = true;
            var missing = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            missing.Children.Add(new FontIcon
            {
                Glyph = "\uE7BA",
                FontSize = 30,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            missing.Children.Add(new TextBlock
            {
                Text = card.HasImage ? "图片文件缺失，请先完成同步" : "无图型号",
                FontSize = 12.5,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            imageHost.Children.Add(missing);
        }

        body.Children.Add(imageHost);

        var info = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
        };
        info.Children.Add(new TextBlock
        {
            Text = card.Name,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        info.Children.Add(Field("型号", card.Model));
        info.Children.Add(Field("品牌", card.Brand));
        info.Children.Add(Field("分类", card.Category));
        if (!string.IsNullOrWhiteSpace(card.ImageFileName))
        {
            info.Children.Add(Field("图片文件", card.ImageFileName!));
        }

        var favoriteButton = new Button { Content = card.IsFavorite ? "取消收藏" : "加入收藏" };
        var copyImageButton = new Button { Content = "复制图片", IsEnabled = card.HasImage };
        var copyNameButton = new Button { Content = "复制名称" };
        var openLocationButton = new Button { Content = "打开文件位置", IsEnabled = card.HasImage };

        // 2×2 网格：四个动作在窄列内也能完整显示，不会被弹层边缘裁掉。
        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnSpacing = 8, RowSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddAction(actions, favoriteButton, 0, 0);
        AddAction(actions, copyImageButton, 0, 1);
        AddAction(actions, copyNameButton, 1, 0);
        AddAction(actions, openLocationButton, 1, 1);
        info.Children.Add(actions);

        if (imageBytesWarning && card.HasImage)
        {
            info.Children.Add(new TextBlock
            {
                Text = "数据库记录了图片，但本地文件不存在——通常是同步未完成或图片目录被移动。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }

        Grid.SetColumn(info, 1);
        body.Children.Add(info);

        favoriteButton.Click += async (_, _) =>
        {
            try
            {
                var newState = toggleFavorite is not null
                    ? await toggleFavorite(card)
                    : await App.Services.GetRequiredService<IFavoriteService>().ToggleAsync(card.ProductId);
                card.IsFavorite = newState;
                favoriteButton.Content = newState ? "取消收藏" : "加入收藏";
            }
            catch (Exception exception)
            {
                favoriteButton.Content = $"收藏切换失败：{exception.Message}";
            }
        };

        copyImageButton.Click += async (_, _) =>
        {
            var result = await App.Services.GetRequiredService<CardActionService>().CopyImageAsync(card);
            copyImageButton.Content = result.Success ? "已复制图片" : "复制失败";
        };

        copyNameButton.Click += async (_, _) =>
        {
            var result = await App.Services.GetRequiredService<CardActionService>()
                .CopyTextAsync(card, CardTextKind.Name);
            copyNameButton.Content = result.Success ? "已复制名称" : "复制失败";
        };

        openLocationButton.Click += (_, _) => OpenLocation(card, openLocationButton);
        return body;
    }

    private static void AddAction(Grid grid, Button button, int row, int column)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private static TextBlock Field(string label, string value)
    {
        return new TextBlock
        {
            Text = $"{label}：{value}",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
    }

    private static void OpenLocation(ProductCard card, Button button)
    {
        var absolute = App.Services.GetRequiredService<IImageStore>().ResolveAbsolutePath(card.ImagePath);
        if (string.IsNullOrWhiteSpace(absolute) || !File.Exists(absolute))
        {
            button.Content = "图片文件不存在";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{absolute}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            button.Content = $"打开失败：{exception.Message}";
        }
    }
}
