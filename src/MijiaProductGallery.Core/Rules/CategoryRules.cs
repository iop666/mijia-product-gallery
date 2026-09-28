namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 归类规则：型号归属大类只按"出现在哪个分类的产品列表"判定，
/// 产品对象自带的 ptId 与分类 ptId 不是同一含义，一律不参与判定。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static class CategoryRules
{
    /// <summary>「新上线」滚动上新货架的分类 ptId。</summary>
    public const int ShelfPtId = -10000;

    /// <summary>「新上线」分类名。</summary>
    public const string ShelfCategoryName = "新上线";

    /// <summary>无法解析归属时的分类名。</summary>
    public const string UnknownCategoryName = "未分类";

    /// <summary>
    /// 解析归属大类。真实大类优先于货架（货架型号同时挂在真实大类时归真实大类）；
    /// 仅出现在货架时归「新上线」；成员为空归「未分类」。
    /// 理论上一个型号只属一个真实大类，若出现多个则取最小 ptId 以保证结果确定性。
    /// </summary>
    public static string ResolveCategory(
        IEnumerable<int> membershipPtIds,
        IReadOnlyDictionary<int, string> ptIdToCategoryName)
    {
        ArgumentNullException.ThrowIfNull(membershipPtIds);
        ArgumentNullException.ThrowIfNull(ptIdToCategoryName);

        int? realPtId = null;
        var onShelf = false;
        foreach (var ptId in membershipPtIds)
        {
            if (ptId == ShelfPtId)
            {
                onShelf = true;
                continue;
            }

            if (realPtId is not { } current || ptId < current)
            {
                realPtId = ptId;
            }
        }

        if (realPtId is int resolved)
        {
            return ptIdToCategoryName.TryGetValue(resolved, out var name)
                ? name
                : $"{UnknownCategoryName}({resolved})";
        }

        return onShelf ? ShelfCategoryName : UnknownCategoryName;
    }
}
