namespace MijiaProductGallery.Core.Models;

/// <summary>应用设置键值对（用户数据）。Value 为 JSON 序列化结果。</summary>
public sealed record AppSettingEntry
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}
