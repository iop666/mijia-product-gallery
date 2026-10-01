namespace MijiaProductGallery.ViewModels;

/// <summary>使用说明分组（标题 + 图标 + 一行条目）。</summary>
public sealed record HelpGroup(string Title, string Icon, IReadOnlyList<string> Items);

/// <summary>设置页"使用说明"内容（集中定义，界面绑定与测试共用）。</summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpGroup> Groups { get; } =
    [
        new HelpGroup("产品卡片", "\uE71D",
        [
            "左键点击：查看产品详情",
            "左键按住并拖出：直接拖拽原图到文件夹或支持图片输入的软件",
            "右键：打开操作菜单",
        ]),
        new HelpGroup("右键菜单", "\uE712",
        [
            "复制图片：复制图片到剪贴板",
            "复制产品名称：复制设备名称文本",
            "复制型号：复制 Model",
            "复制品牌：复制品牌名称",
            "复制完整信息：复制完整产品信息",
        ]),
        new HelpGroup("收藏", "\uE735",
        [
            "点击 / 右键收藏：加入默认收藏（星标）",
            "加入收藏：可以加入多个收藏夹",
            "收藏夹：可以创建、重命名、删除和导出",
        ]),
        new HelpGroup("搜索", "\uE721",
        [
            "Ctrl + F：快速聚焦搜索框",
        ]),
        new HelpGroup("分页模式", "\uE8AD",
        [
            "← / →：上一页 / 下一页",
            "PageUp / PageDown：上一页 / 下一页",
            "Home：第一页；End：最后一页",
            "页码框：输入页码后按 Enter 直接跳转",
        ]),
        new HelpGroup("连续滚动模式", "\uE8B1",
        [
            "鼠标滚轮：连续浏览产品",
        ]),
        new HelpGroup("筛选", "\uE71C",
        [
            "\"筛选\"按钮：打开右侧筛选面板",
            "Esc：关闭筛选面板",
        ]),
        new HelpGroup("首次启动与同步", "\uE895",
        [
            "首次启动：显示控制台加载窗口，获取产品与 10000+ 张示例样图，完成后自动进入图库",
            "加载窗口期间请勿关闭：关闭窗口会中止初始化，重新启动应用即可继续",
            "同步中心：同步进行时显示实时进度、当前阶段、已用时间与剩余时间",
            "数据变化（新增/换图/下架等）同步完成后，筛选面板的分类与品牌自动刷新",
        ]),
        new HelpGroup("其他", "\uE713",
        [
            "收藏夹：可以创建、重命名、删除和导出 ZIP",
        ]),
    ];
}
