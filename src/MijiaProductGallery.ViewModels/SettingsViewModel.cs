using System.Reflection;
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
    private readonly object writeLock = new();
    private Task writeChain = Task.CompletedTask;
    private bool loading;

    public SettingsViewModel(
        ISettingsRepository settings,
        IRecentService? recentService = null,
        IAppDataRoot? dataRoot = null)
    {
        this.settings = settings;
        this.recentService = recentService;
        DataRoot = dataRoot?.RootPath ?? string.Empty;
    }

    /// <summary>设置持久化失败（界面提示，不中断操作）。</summary>
    public event Action<string>? SaveFailed;

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

    /// <summary>应用版本（来自入口程序集的 InformationalVersion，去掉构建元数据后缀）。</summary>
    public string AppVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "未知";
        }
    }

    /// <summary>数据源说明。</summary>
    public string DataSourceDescription => "米家百科公开产品库（home.mi.com）。图片版权归小米公司及相关厂商所有。";

    partial void OnThemeChanged(string value)
    {
        ThemeChanged?.Invoke(value);
        if (loading)
        {
            return;
        }

        QueueSave(AppSettingsKeys.Theme, value);
    }

    partial void OnLaunchViewChanged(string value)
    {
        if (!loading)
        {
            QueueSave(AppSettingsKeys.LaunchView, value);
        }
    }

    partial void OnBrowseModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPagedBrowseMode));
        if (loading)
        {
            return;
        }

        BrowseModeChanged?.Invoke(value);
        QueueSave(AppSettingsKeys.GalleryBrowseMode, value);
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

        if (loading)
        {
            return;
        }

        PageSizeChanged?.Invoke(value);
        QueueSave(AppSettingsKeys.GalleryPageSize, value);
    }

    partial void OnRecordViewsChanged(bool value)
    {
        if (!loading)
        {
            QueueSave(AppSettingsKeys.RecordViews, value);
        }
    }

    partial void OnRecordCopiesChanged(bool value)
    {
        if (!loading)
        {
            QueueSave(AppSettingsKeys.RecordCopies, value);
        }
    }

    partial void OnRecordDragsChanged(bool value)
    {
        if (!loading)
        {
            QueueSave(AppSettingsKeys.RecordDrags, value);
        }
    }

    /// <summary>串行化设置写入：连续切换时后写覆盖先写，避免乱序落库。</summary>
    private void QueueSave<T>(string key, T value)
        where T : notnull
    {
        lock (writeLock)
        {
            // 无排队写入时直接发起，保留同步完成的实现（测试与本地 SQLite 都不引入额外线程跳转）。
            writeChain = writeChain.IsCompleted
                ? SaveCoreAsync(key, value)
                : writeChain.ContinueWith(
                    _ => SaveCoreAsync(key, value),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
        }
    }

    private async Task SaveCoreAsync<T>(string key, T value)
        where T : notnull
    {
        try
        {
            await settings.SetValueAsync(key, value);
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke($"设置保存失败：{exception.Message}");
        }
    }

    /// <summary>等待已排队的设置写入完成（页面离开/应用退出前可调用）。</summary>
    public Task FlushAsync()
    {
        lock (writeLock)
        {
            return writeChain;
        }
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
        // 回填阶段不触发持久化与图库重载事件：打开设置页只是读，不应写库或刷新图库。
        loading = true;
        try
        {
            Theme = await settings.GetValueAsync(AppSettingsKeys.Theme, AppSettingsKeys.ThemeDefault, cancellationToken);
            LaunchView = await settings.GetValueAsync(AppSettingsKeys.LaunchView, AppSettingsKeys.LaunchViewDefault, cancellationToken);
            BrowseMode = await settings.GetValueAsync(AppSettingsKeys.GalleryBrowseMode, AppSettingsKeys.GalleryBrowseModeDefault, cancellationToken);
            PageSize = await settings.GetValueAsync(AppSettingsKeys.GalleryPageSize, AppSettingsKeys.GalleryPageSizeDefault, cancellationToken);
            RecordViews = await settings.GetValueAsync(AppSettingsKeys.RecordViews, true, cancellationToken);
            RecordCopies = await settings.GetValueAsync(AppSettingsKeys.RecordCopies, true, cancellationToken);
            RecordDrags = await settings.GetValueAsync(AppSettingsKeys.RecordDrags, true, cancellationToken);
        }
        finally
        {
            loading = false;
        }
    }

}
