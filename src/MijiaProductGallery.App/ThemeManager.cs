using Microsoft.UI.Xaml;
using MijiaProductGallery.Core;

namespace MijiaProductGallery.App;

/// <summary>
/// 主题状态中枢：保存当前生效主题，供弹窗/对话框在打开时对齐主题
/// （弹窗不在根元素子树内，元素级主题翻转对其无效，需显式设置 RequestedTheme）。
/// </summary>
public static class ThemeManager
{
    /// <summary>当前生效主题（System/Light/Dark/Gray），切换时由主窗口更新。</summary>
    public static string CurrentTheme { get; set; } = AppSettingsKeys.ThemeDefault;

    /// <summary>菜单类弹窗不在根元素子树内：打开时逐项对齐当前主题。</summary>
    public static void ApplyToFlyout(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase flyout)
    {
        var theme = ToElementTheme(CurrentTheme);
        if (flyout is Microsoft.UI.Xaml.Controls.MenuFlyout menu)
        {
            ApplyToMenuItems(menu.Items, theme);
        }
    }

    private static void ApplyToMenuItems(
        System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase> items,
        ElementTheme theme)
    {
        foreach (var item in items)
        {
            if (item is Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem sub)
            {
                ApplyToMenuItems(sub.Items, theme);
            }

            if (item is Microsoft.UI.Xaml.FrameworkElement element)
            {
                element.RequestedTheme = theme;
            }
        }
    }

    /// <summary>主题字符串 → 根元素主题（灰色为深色变体）。</summary>
    public static ElementTheme ToElementTheme(string? theme)
    {
        return theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" or "Gray" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
