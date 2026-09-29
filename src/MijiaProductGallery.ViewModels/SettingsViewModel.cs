using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>设置页视图模型：常规/图库/行为/数据/同步/关于六区（数据区展示目录与清空历史）。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository settings;
    private readonly IRecentService? recentService;
    public SettingsViewModel(
        ISettingsRepository settings,
        IRecentService? recentService = null)
    {
        this.settings = settings;
        this.recentService = recentService;
    }

    /// <summary>主题变更事件（App 层订阅并应用到根视觉树）。</summary>
    public event Action<string>? ThemeChanged;

    /// <summary>每页显示数量变更事件（图库视图模型订阅并重新分页）。</summary>
    public event Action<int>? PageSizeChanged;

    /// <summary>浏览模式变更事件（图库视图模型订阅并重新加载）。</summary>
    public event Action<string>? BrowseModeChanged;

    /// <summary>是否分页模式（决定"每页显示数量"设置是否可用）。</summary>
    public bool IsPagedBrowseMode => BrowseMode != AppSettingsKeys.GalleryBrowseModeContinuous;

    /// <summary>设置页"使用说明"分组内容。</summary>
    public IReadOnlyList<HelpGroup> HelpGroups => HelpContent.Groups;

    /// <summary>连续滚动模式提示文案。</summary>
    public string PageSizeDisabledHint => "连续滚动模式下不可用";

    /// <summary>数据根目录（只读展示，由 App 注入）。</summary>
    public string DataRoot { get; set; } = string.Empty;

    [ObservableProperty]
    private string theme = AppSettingsKeys.ThemeDefault;

    [ObservableProperty]
    private string launchView = AppSettingsKeys.LaunchViewDefault;

    /// <summary>浏览模式（Paged 分页默认 / Continuous 连续滚动）。</summary>
    [ObservableProperty]
    private string browseMode = AppSettingsKeys.GalleryBrowseModeDefault;

    /// <summary>每页显示数量（9～140，默认 21）。</summary>
    [ObservableProperty]
    private int pageSize = AppSettingsKeys.GalleryPageSizeDefault;

    [ObservableProperty]
    private bool recordViews = true;

    [ObservableProperty]
    private bool recordCopies = true;

    [ObservableProperty]
    private bool recordDrags = true;

    /// <summary>应用版本（来自程序集）。</summary>
    public string AppVersion => "0.1.0";

    /// <summary>数据源说明。</summary>
    public string DataSourceDescription => "米家百科公开产品库（home.mi.com）。图片版权归小米公司及相关厂商所有。";

    partial void OnThemeChanged(string value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.Theme, value);
        ThemeChanged?.Invoke(value);
    }

    partial void OnLaunchViewChanged(string value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.LaunchView, value);
    }

    partial void OnBrowseModeChanged(string value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.GalleryBrowseMode, value);
        OnPropertyChanged(nameof(IsPagedBrowseMode));
        BrowseModeChanged?.Invoke(value);
    }

    partial void OnPageSizeChanged(int value)
    {
        var clamped = Math.Clamp(value, AppSettingsKeys.GalleryPageSizeMin, AppSettingsKeys.GalleryPageSizeMax);
        if (clamped != value)
        {
            // 越界输入收敛到合法范围（只影响持久化与事件，界面由调用方刷新）。
            PageSize = clamped;
            return;
        }

        _ = settings.SetValueAsync(AppSettingsKeys.GalleryPageSize, value);
        PageSizeChanged?.Invoke(value);
    }

    partial void OnRecordViewsChanged(bool value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.RecordViews, value);
    }

    partial void OnRecordCopiesChanged(bool value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.RecordCopies, value);
    }

    partial void OnRecordDragsChanged(bool value)
    {
        _ = settings.SetValueAsync(AppSettingsKeys.RecordDrags, value);
    }

    /// <summary>清空最近使用记录（仅 UsageEvents/ProductUsages；收藏与搜索历史不受影响）。</summary>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (recentService is not null)
        {
            await recentService.ClearHistoryAsync(cancellationToken);
        }
    }

    /// <summary>加载全部设置（页面进入时）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Theme = await settings.GetValueAsync(AppSettingsKeys.Theme, AppSettingsKeys.ThemeDefault, cancellationToken);
        LaunchView = await settings.GetValueAsync(AppSettingsKeys.LaunchView, AppSettingsKeys.LaunchViewDefault, cancellationToken);
        BrowseMode = await settings.GetValueAsync(AppSettingsKeys.GalleryBrowseMode, AppSettingsKeys.GalleryBrowseModeDefault, cancellationToken);
        PageSize = await settings.GetValueAsync(AppSettingsKeys.GalleryPageSize, AppSettingsKeys.GalleryPageSizeDefault, cancellationToken);
        RecordViews = await settings.GetValueAsync(AppSettingsKeys.RecordViews, true, cancellationToken);
        RecordCopies = await settings.GetValueAsync(AppSettingsKeys.RecordCopies, true, cancellationToken);
        RecordDrags = await settings.GetValueAsync(AppSettingsKeys.RecordDrags, true, cancellationToken);
    }

}
