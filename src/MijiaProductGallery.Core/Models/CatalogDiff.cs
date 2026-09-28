namespace MijiaProductGallery.Core.Models;

/// <summary>一轮全量对比的结论，变更按型号 Ordinal 顺序排列。</summary>
public sealed record CatalogDiff
{
    public static readonly CatalogDiff Empty = new() { Changes = [] };

    public required IReadOnlyList<ProductChange> Changes { get; init; }

    public int Count(Enums.ChangeType type)
    {
        var n = 0;
        foreach (var change in Changes)
        {
            if (change.Type == type)
            {
                n++;
            }
        }

        return n;
    }
}
