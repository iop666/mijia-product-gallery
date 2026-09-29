using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MijiaProductGallery.App;

/// <summary>
/// 灰色调色板（Adobe 风格中灰界面）：仅灰色模式挂载应用级画笔覆盖（元素主题为 Dark），
/// 使黑色系设备图片不融入纯黑背景；切回其他主题时移除覆盖。
/// 注意：不能通过 App 级 ThemeDictionaries 实现——空的 Dark 字典会遮蔽整个深色主题资源。
/// </summary>
public static class ThemePalette
{
    private static readonly (string Key, uint Argb)[] GrayPalette =
    {
        ("ApplicationPageBackgroundThemeBrush", 0xFF2D2D2D),
        ("SolidBackgroundFillColorBase", 0xFF2D2D2D),
        ("SolidBackgroundFillColorSecondary", 0xFF323232),
        ("SolidBackgroundFillColorTertiary", 0xFF383838),
        ("LayerFillColorDefault", 0xFF303030),
        ("LayerFillColorSecondary", 0xFF2A2A2A),
        ("CardBackgroundFillColorDefault", 0xFF333333),
        ("CardBackgroundFillColorSecondary", 0xFF383838),
        ("CardStrokeColorDefault", 0xFF212121),
        ("ControlFillColorDefault", 0xFF333333),
    };

    public static void Apply(string? theme)
    {
        var resources = Application.Current.Resources;
        if (theme == "Gray")
        {
            foreach (var (key, argb) in GrayPalette)
            {
                resources[key] = Solid(argb);
            }
        }
        else
        {
            foreach (var (key, _) in GrayPalette)
            {
                resources.Remove(key);
            }
        }
    }

    private static Brush Solid(uint argb)
    {
        return new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
    }
}
