namespace MijiaProductGallery.Core.Models;

/// <summary>首次初始化结局。</summary>
public enum InitializationOutcome
{
    /// <summary>本地数据库已有产品，无需初始化。</summary>
    AlreadyInitialized,

    /// <summary>从 Seed Package 完成初始化。</summary>
    CompletedFromSeed,

    /// <summary>无种子包，通过在线全量同步完成初始化。</summary>
    CompletedOnline,

    /// <summary>无数据库数据、无种子包且网络不可用：显式初始化失败状态（不是正常空图库）。</summary>
    FailedNoSeedOffline,

    /// <summary>种子包存在但导入失败（包损坏/校验不通过等）。</summary>
    FailedImport,
}

/// <summary>首次初始化报告：未来 UI 据此渲染初始化页与失败态的操作项。</summary>
public sealed record InitializationReport
{
    public required InitializationOutcome Outcome { get; init; }

    /// <summary>导入/在线同步得到的快照日期。</summary>
    public string? SnapshotDate { get; init; }

    public SeedImportResult? SeedResult { get; init; }

    /// <summary>失败原因或补充说明。</summary>
    public string? Message { get; init; }

    /// <summary>失败态下可执行的操作：retry（重试）/ check-seed（检查种子包）/ online-init（在线初始化）。</summary>
    public IReadOnlyList<string> AvailableActions { get; init; } = [];
}
