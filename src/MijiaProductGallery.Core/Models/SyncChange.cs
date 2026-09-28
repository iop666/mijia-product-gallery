using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>同步产生的单条变更留痕（含判定依据，可追溯）。</summary>
public sealed record SyncChange
{
    public long Id { get; init; }

    public required long SyncRunId { get; init; }

    /// <summary>变更涉及的型号（弱关联文本，不设外键，留痕不随产品增删变化）。</summary>
    public required string Model { get; init; }

    /// <summary>变更类型标量列，用于索引统计；完整结论在 Change 中。</summary>
    public required ChangeType Type { get; init; }

    public required ProductChange Change { get; init; }
}
