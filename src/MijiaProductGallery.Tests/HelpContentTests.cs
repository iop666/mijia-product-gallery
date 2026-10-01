using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>设置页"使用说明"内容：分组齐全、关键鼠标/键盘行为均有说明。</summary>
public sealed class HelpContentTests
{
    private static string AllText() =>
        string.Join("\n", HelpContent.Groups.SelectMany(g =>
            new[] { g.Title }.Concat(g.Items)));

    [Fact]
    public void Groups_CoverExpectedSections()
    {
        var titles = HelpContent.Groups.Select(g => g.Title).ToList();
        Assert.Equal(
            ["产品卡片", "右键菜单", "收藏", "搜索", "分页模式", "连续滚动模式", "筛选", "首次启动与同步", "其他"],
            titles);
    }

    [Fact]
    public void CardGroup_CoversLeftClick_Drag_Context()
    {
        var text = AllText();
        Assert.Contains("左键点击：查看产品详情", text);
        Assert.Contains("拖拽原图", text);
        Assert.Contains("右键：打开操作菜单", text);
    }

    [Fact]
    public void ContextMenuGroup_ListsAllCopyActions()
    {
        var text = AllText();
        Assert.Contains("复制图片", text);
        Assert.Contains("复制产品名称", text);
        Assert.Contains("复制型号", text);
        Assert.Contains("复制品牌", text);
        Assert.Contains("复制完整信息", text);
    }

    [Fact]
    public void CollectionGroup_MentionsDefaultAndMultiple()
    {
        var text = AllText();
        Assert.Contains("默认收藏", text);
        Assert.Contains("多个收藏夹", text);
        Assert.Contains("导出", text);
    }

    [Fact]
    public void PagingGroup_CoversKeyboardAndPageBox()
    {
        var text = AllText();
        Assert.Contains("← / →", text);
        Assert.Contains("PageUp / PageDown", text);
        Assert.Contains("Home", text);
        Assert.Contains("End", text);
        Assert.Contains("Enter 直接跳转", text);
    }

    [Fact]
    public void SearchAndFilter_CoverCtrlF_AndEsc()
    {
        var text = AllText();
        Assert.Contains("Ctrl + F", text);
        Assert.Contains("Esc：关闭筛选面板", text);
    }

    [Fact]
    public void EveryGroup_HasIcon_AndAtLeastOneItem()
    {
        Assert.All(HelpContent.Groups, group =>
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Icon));
            Assert.NotEmpty(group.Items);
            Assert.All(group.Items, item => Assert.NotEmpty(item));
        });
    }
}
