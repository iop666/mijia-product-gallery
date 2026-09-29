using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MijiaProductGallery.App.Converters;

/// <summary>布尔 → Visibility 转换器。</summary>
public sealed class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

/// <summary>布尔反转 → Visibility 转换器（true 折叠）。</summary>
public sealed class InverseBoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

/// <summary>布尔反转转换器（true → false），用于 IsEnabled 等布尔属性。</summary>
public sealed class InverseBoolConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is not true;
    }
}

/// <summary>null → Collapsed 转换器（可选文本行使用）。</summary>
public sealed class NullToCollapsedConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 缩略图磁盘路径 → BitmapImage 转换器（空路径返回 null）。
/// 经 LRU 缓存复用图像实例：快速滚动中卡片反复实化不再触发重复 IO/解码。
/// 解码按卡片显示尺寸约束（逻辑像素），高 DPI 下仍清晰且省内存。
/// </summary>
public sealed class PathToImageConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    private static readonly MijiaProductGallery.App.Services.ThumbnailImageCache Cache = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string path && !string.IsNullOrWhiteSpace(path)
            ? Cache.GetOrAdd(path, p => new BitmapImage(new Uri(p))
            {
                DecodePixelType = DecodePixelType.Logical,
                // 卡片显示宽约 160~184 逻辑像素；320 在 200% DPI 下 1:1 清晰。
                // 实测（LRU 240 满载、连续滚动 600+ 缺刻）：整进程 WS ≈190MB / Private ≈188MB，
                // 已处于目标区间；更大的解码面或更小容量均未带来可复现的收益。
                DecodePixelWidth = 320,
            })
            : null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
