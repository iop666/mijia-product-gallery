using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MijiaProductGallery.Core;
using Windows.UI;

namespace MijiaProductGallery.App;

/// <summary>
/// 主题画笔应用器：把窗口/图层/导航窗格背景覆盖注册到应用资源的 MergedDictionaries
/// （携带 Light/Dark 两套主题字典），实现不重启的实时主题切换。
/// 放在 XamlControlsResources 之后的合并字典只覆盖显式提供的键，缺失键回落到完整默认主题，
/// 因此不会出现"部分资源退化为浅色"的混色问题。灰色模式 = 深色主题字典的 Adobe 中灰取值。
/// </summary>
public static class ThemePalette
{
    private static ResourceDictionary? registered;

    public static void Apply(string? theme)
    {
        var resources = Application.Current.Resources;
        if (registered is null)
        {
            registered = new ResourceDictionary();
            registered.ThemeDictionaries["Light"] = Build(ThemePaletteDefinition.For("Light"));
            registered.ThemeDictionaries["Dark"] = Build(ThemePaletteDefinition.For("Dark"));
            resources.MergedDictionaries.Add(registered);
        }

        // 深色与灰色的差异都体现在 Dark 主题字典：
        // 深色→灰色（元素主题不变、无主题变更信号）由主窗口借浅色过渡强制重解析。
        var darkValues = theme == "Gray" ? ThemePaletteDefinition.For("Gray") : ThemePaletteDefinition.For("Dark");
        var dark = (ResourceDictionary)registered.ThemeDictionaries["Dark"];
        dark.Clear();
        foreach (var pair in darkValues)
        {
            dark[pair.Key] = Solid(pair.Value);
        }
    }

    private static ResourceDictionary Build(IReadOnlyDictionary<string, uint> palette)
    {
        var dict = new ResourceDictionary();
        foreach (var pair in palette)
        {
            dict[pair.Key] = Solid(pair.Value);
        }

        return dict;
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
