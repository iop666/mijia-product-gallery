using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MijiaProductGallery.App.Converters;

/// <summary>x:Bind 静态转换函数集。</summary>
public static class UiConverters
{
    public static Visibility ToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility ToInverseVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>缩略图磁盘路径 → BitmapImage（空路径返回 null 以免触发绑定错误）。</summary>
    public static BitmapImage? ToBitmapImage(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : new BitmapImage(new Uri(path));
    }
}
