namespace MijiaProductGallery.Core;

/// <summary>
/// 窗口级画笔覆盖表：主题为应用级资源，元素级 RequestedTheme 无法翻转窗口/图层背景，
/// 切换主题时按目标主题把下列键写入/更新到应用级资源（纯数据，便于单元测试）。
/// </summary>
public static class ThemePaletteDefinition
{
    /// <summary>覆盖键集合（窗口背景、实底背景、图层背景、导航窗格背景）。</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "ApplicationPageBackgroundThemeBrush",
        "SolidBackgroundFillColorBase",
        "SolidBackgroundFillColorSecondary",
        "SolidBackgroundFillColorTertiary",
        "LayerFillColorDefault",
        "LayerFillColorSecondary",
        "NavigationViewContentBackground",
        "NavigationViewExpandedPaneBackground",
    ];

    /// <summary>返回指定主题的覆盖表（键 → ARGB）。System 为空表（完全跟随系统）。</summary>
    public static IReadOnlyDictionary<string, uint> For(string? theme)
    {
        return theme switch
        {
            "Dark" => new Dictionary<string, uint>
            {
                ["ApplicationPageBackgroundThemeBrush"] = 0xFF202020,
                ["SolidBackgroundFillColorBase"] = 0xFF1C1C1C,
                ["SolidBackgroundFillColorSecondary"] = 0xFF202020,
                ["SolidBackgroundFillColorTertiary"] = 0xFF242424,
                ["LayerFillColorDefault"] = 0xFF2C2C2C,
                ["LayerFillColorSecondary"] = 0xFF282828,
                ["NavigationViewContentBackground"] = 0xFF202020,
                ["NavigationViewExpandedPaneBackground"] = 0xFF272727,
            },
            "Gray" => new Dictionary<string, uint>
            {
                ["ApplicationPageBackgroundThemeBrush"] = 0xFF2D2D2D,
                ["SolidBackgroundFillColorBase"] = 0xFF2D2D2D,
                ["SolidBackgroundFillColorSecondary"] = 0xFF323232,
                ["SolidBackgroundFillColorTertiary"] = 0xFF383838,
                ["LayerFillColorDefault"] = 0xFF303030,
                ["LayerFillColorSecondary"] = 0xFF2A2A2A,
                ["NavigationViewContentBackground"] = 0xFF2D2D2D,
                ["NavigationViewExpandedPaneBackground"] = 0xFF333333,
            },
            "Light" => new Dictionary<string, uint>
            {
                ["ApplicationPageBackgroundThemeBrush"] = 0xFFFFFFFF,
                ["SolidBackgroundFillColorBase"] = 0xFFFFFFFF,
                ["SolidBackgroundFillColorSecondary"] = 0xFFF9F9F9,
                ["SolidBackgroundFillColorTertiary"] = 0xFFFFFFFF,
                ["LayerFillColorDefault"] = 0xFFFFFFFF,
                ["LayerFillColorSecondary"] = 0xFFF5F5F5,
                ["NavigationViewContentBackground"] = 0xFFF3F3F3,
                ["NavigationViewExpandedPaneBackground"] = 0xFFF7F7F7,
            },
            _ => new Dictionary<string, uint>(),
        };
    }
}
