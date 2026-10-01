using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>
/// 同步实时进度：引擎在各阶段按节流频率上报（工作线程触发，订阅方自行调度 UI 线程）。
/// Done/Total 为当前阶段内的计数；OverallPercent 为按阶段权重估算的全流程百分比。
/// </summary>
public sealed record SyncProgress
{
    /// <summary>阶段总数（不含 Completed）。</summary>
    public const int TotalStages = 6;

    public required SyncStatus Status { get; init; }

    public required SyncStage Stage { get; init; }

    /// <summary>阶段序号（按执行顺序 1-6；NotStarted 为 0，Completed 为 TotalStages+1）。</summary>
    public int StageIndex { get; init; }

    /// <summary>阶段内明细（如"分类 3/43"）。</summary>
    public string? Detail { get; init; }

    /// <summary>当前阶段已完成计数（无计数的阶段为 0）。</summary>
    public int Done { get; init; }

    /// <summary>当前阶段总数（未知为 0）。</summary>
    public int Total { get; init; }

    /// <summary>本轮已进行秒数。</summary>
    public double ElapsedSeconds { get; init; }

    /// <summary>全流程估算百分比（0-100）；不可估时为 null。</summary>
    public double? OverallPercent { get; init; }

    /// <summary>累计图片下载/落盘失败数。</summary>
    public int Failures { get; init; }

    /// <summary>终态时的变更统计（运行中为 null）。</summary>
    public SyncRunCounts? Counts { get; init; }

    /// <summary>终态失败原因。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>按执行顺序的阶段序号（1 起；枚举定义顺序与执行顺序不同，禁止直接用枚举值）。</summary>
    public static int StageOrder(SyncStage stage)
    {
        return stage switch
        {
            SyncStage.FetchingCategories => 1,
            SyncStage.FetchingProducts => 2,
            SyncStage.Comparing => 3,
            SyncStage.DownloadingImages => 4,
            SyncStage.UpdatingDatabase => 5,
            SyncStage.GeneratingThumbnails => 6,
            SyncStage.Completed => TotalStages + 1,
            _ => 0,
        };
    }

    /// <summary>各阶段权重（%），总和 100：下载图片为主要耗时，更新数据库与缩略图次之。</summary>
    private static readonly int[] StageWeights = [3, 10, 2, 60, 15, 10];

    /// <summary>按阶段与阶段内进度估算全流程百分比（0-100）。</summary>
    public static double? EstimateOverallPercent(SyncStage stage, int done, int total)
    {
        var order = StageOrder(stage);
        if (order <= 0)
        {
            return 0;
        }

        if (stage == SyncStage.Completed)
        {
            return 100;
        }

        var completedWeight = 0;
        for (var i = 0; i < order - 1; i++)
        {
            completedWeight += StageWeights[i];
        }

        var fraction = total > 0 ? Math.Clamp((double)done / total, 0, 1) : 0;
        return Math.Round(completedWeight + StageWeights[order - 1] * fraction, 1);
    }
}
