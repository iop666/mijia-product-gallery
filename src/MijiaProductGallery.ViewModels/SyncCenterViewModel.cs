using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>同步中心视图模型：状态/阶段/上次同步/变更计数展示与手动触发、取消、自动频率设置。</summary>
public partial class SyncCenterViewModel : ObservableObject
{
    public const string AutoIntervalSettingsKey = "Sync.AutoInterval";

    private readonly ISyncService syncService;
    private readonly ISyncStateRepository syncStateRepository;
    private readonly ISettingsRepository settings;

    public SyncCenterViewModel(
        ISyncService syncService,
        ISyncStateRepository syncStateRepository,
        ISettingsRepository settings)
    {
        this.syncService = syncService;
        this.syncStateRepository = syncStateRepository;
        this.settings = settings;
    }

    /// <summary>是否正在同步（驱动按钮可用性与进度条）。</summary>
    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "空闲";

    [ObservableProperty]
    private string stageText = string.Empty;

    [ObservableProperty]
    private string lastSyncText = "从未同步";

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>最近一轮变更统计摘要（如"新增 3 · 下架 1"），无变更时为空。</summary>
    [ObservableProperty]
    private string? lastRunSummary;

    [ObservableProperty]
    private SyncAutoInterval autoInterval = SyncAutoInterval.Daily;

    [ObservableProperty]
    private bool isLoadingSettings = true;

    /// <summary>AutoIntervalIndex 绑定值（ComboBox SelectedIndex 0-4）；赋值即持久化（不等待完成）。</summary>
    public int AutoIntervalIndex
    {
        get => (int)AutoInterval;
        set
        {
            AutoInterval = (SyncAutoInterval)value;
            _ = SetAutoIntervalAsync((SyncAutoInterval)value);
        }
    }

    /// <summary>是否有错误可展示。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>是否有最近一轮摘要可展示。</summary>
    public bool HasLastRunSummary => !string.IsNullOrEmpty(LastRunSummary);

    /// <summary>触发一次手动同步（立即同步与重试共用）。</summary>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _ = await syncService.SyncNowAsync(SyncTrigger.Manual, cancellationToken);
        await RefreshAsync(cancellationToken);
        IsRunning = false;
    }

    /// <summary>请求取消当前同步（引擎将本轮标记失败，可重试）。</summary>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        await syncService.CancelAsync(cancellationToken);
    }

    /// <summary>读取同步状态与最近一轮记录（供周期轮询）。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = await syncStateRepository.GetStateAsync(cancellationToken);
        StatusText = state.Status switch
        {
            SyncStatus.Idle => "空闲",
            SyncStatus.Running => "同步中",
            SyncStatus.Success => "成功",
            SyncStatus.Failed => "失败",
            _ => state.Status.ToString(),
        };
        StageText = state.Stage switch
        {
            SyncStage.NotStarted => string.Empty,
            SyncStage.FetchingCategories => "获取分类",
            SyncStage.FetchingProducts => "获取产品",
            SyncStage.Comparing => "对比数据",
            SyncStage.DownloadingImages => "下载图片",
            SyncStage.GeneratingThumbnails => "生成缩略图",
            SyncStage.UpdatingDatabase => "更新数据库",
            SyncStage.Completed => "完成",
            _ => string.Empty,
        };
        ErrorMessage = state.ErrorMessage;
        OnPropertyChanged(nameof(HasError));
        LastSyncText = state.LastSyncUnix is null
            ? "从未同步"
            : $"本地 {DateTimeOffset.FromUnixTimeSeconds(state.LastSyncUnix.Value).ToLocalTime():yyyy-MM-dd HH:mm}";

        var latestRun = await syncStateRepository.GetLatestRunAsync(cancellationToken);
        if (latestRun?.Counts is { } counts)
        {
            var parts = new List<string>();
            if (counts.NewCount > 0) parts.Add($"新增 {counts.NewCount}");
            if (counts.NameChangedCount > 0) parts.Add($"改名 {counts.NameChangedCount}");
            if (counts.ImageChangedCount > 0) parts.Add($"换图 {counts.ImageChangedCount}");
            if (counts.IdReusedCount > 0) parts.Add($"ID复用 {counts.IdReusedCount}");
            if (counts.DelistedCount > 0) parts.Add($"下架 {counts.DelistedCount}");
            if (counts.ImageFailureCount > 0) parts.Add($"图片失败 {counts.ImageFailureCount}");
            LastRunSummary = parts.Count == 0 ? "无变更" : string.Join(" · ", parts);
        }
        else
        {
            LastRunSummary = null;
        }

        OnPropertyChanged(nameof(HasLastRunSummary));
    }

    /// <summary>加载自动同步频率设置（页面进入时）。</summary>
    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var text = await settings.GetValueAsync(AutoIntervalSettingsKey, nameof(SyncAutoInterval.Daily), cancellationToken);
        AutoInterval = Enum.TryParse<SyncAutoInterval>(text, ignoreCase: true, out var parsed)
            ? parsed
            : SyncAutoInterval.Daily;
        IsLoadingSettings = false;
    }

    /// <summary>保存自动同步频率设置。</summary>
    public async Task SetAutoIntervalAsync(SyncAutoInterval interval, CancellationToken cancellationToken = default)
    {
        AutoInterval = interval;
        await settings.SetValueAsync(AutoIntervalSettingsKey, interval.ToString(), cancellationToken);
    }
}
