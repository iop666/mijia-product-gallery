using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 官网数据与本地数据的对比规则引擎。
/// 纯函数：同一输入必得同一输出；不访问数据库、网络、文件系统与 UI，也不读取当前时间，
/// 全部时间信息由调用方以参数传入。
///
/// 判定顺序遵循图库数据规范（sha 相同即图片无变化；sha 不同时按
/// createTime 变化 / 品牌变化 / 名称指向不同产品识别复用；无法确定时一律按
/// ID 复用处理，保旧图优先；下架型号永不删除，仅标记）。
/// </summary>
public static class ProductCompareRules
{
    /// <summary>对比本地与官网全量产品，产出按型号 Ordinal 排序的变更清单。</summary>
    /// <param name="local">本地全部产品（含下架保留型号）。</param>
    /// <param name="remote">官网当前在架产品。</param>
    /// <param name="imageReviews">已完成下载复核的型号图片材料；未提供时涉及图片的结论为待复核。</param>
    public static CatalogDiff CompareCatalog(
        IReadOnlyList<LocalProductState> local,
        IReadOnlyList<RemoteProductState> remote,
        IReadOnlyDictionary<string, ImageReviewInput>? imageReviews = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var localByModel = new Dictionary<string, LocalProductState>(local.Count, StringComparer.Ordinal);
        foreach (var state in local)
        {
            localByModel[state.Model] = state;
        }

        var remoteByModel = new Dictionary<string, RemoteProductState>(remote.Count, StringComparer.Ordinal);
        foreach (var state in remote)
        {
            remoteByModel[state.Model] = state;
        }

        var models = new SortedSet<string>(localByModel.Keys, StringComparer.Ordinal);
        models.UnionWith(remoteByModel.Keys);

        var changes = new List<ProductChange>(models.Count);
        foreach (var model in models)
        {
            localByModel.TryGetValue(model, out var localState);
            remoteByModel.TryGetValue(model, out var remoteState);

            if (localState is null)
            {
                changes.Add(NewChange(remoteState!));
            }
            else if (remoteState is null)
            {
                changes.Add(DelistedChange(localState));
            }
            else
            {
                ImageReviewInput? review = null;
                if (imageReviews is not null)
                {
                    imageReviews.TryGetValue(model, out review);
                }

                var change = CompareProduct(localState, remoteState, review);
                if (change is not null)
                {
                    changes.Add(change);
                }
            }
        }

        return new CatalogDiff { Changes = changes };
    }

    /// <summary>对比单个同时存在于本地与官网的型号；无任何变化时返回 null。</summary>
    public static ProductChange? CompareProduct(
        LocalProductState local,
        RemoteProductState remote,
        ImageReviewInput? review)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var nameKind = NameSimilarity.Classify(local.Name, remote.Name);
        var brandChanged = !string.Equals(
            NameSimilarity.Normalize(local.Brand),
            NameSimilarity.Normalize(remote.Brand),
            StringComparison.Ordinal);
        var createTimeChanged = local.CreateTimeUnix is { } localCreateTime
            && localCreateTime != remote.CreateTimeUnix;
        var categoryChanged = !string.Equals(local.Category, remote.Category, StringComparison.Ordinal);

        var imageOutcome = ImageOutcome.None;
        string? replacedByOldFileName = null;
        string? affectedImageFileName = null;
        if (review is not null)
        {
            imageOutcome = EvaluateImageChange(
                local.ImageSha256,
                review.DownloadedSha256,
                nameKind,
                brandChanged,
                createTimeChanged,
                review.ExistingOldImageFiles,
                local.ImageFileName,
                out replacedByOldFileName);
            if (imageOutcome is ImageOutcome.ImageChanged or ImageOutcome.IdReused)
            {
                affectedImageFileName = local.ImageFileName;
            }
        }

        var type = ResolvePrimaryChangeType(
            nameKind,
            brandChanged,
            categoryChanged,
            imageOutcome,
            local.IsAvailable);
        if (type is null)
        {
            return null;
        }

        var nameChanged = nameKind != NameChangeKind.None;
        return new ProductChange
        {
            Type = type.Value,
            Model = local.Model,
            OldCategory = categoryChanged ? local.Category : null,
            NewCategory = categoryChanged ? remote.Category : null,
            NameChange = nameKind,
            OldName = nameChanged ? local.Name : null,
            NewName = nameChanged ? remote.Name : null,
            OldBrand = brandChanged ? local.Brand : null,
            NewBrand = brandChanged ? remote.Brand : null,
            ImageOutcome = imageOutcome,
            ImageFileName = affectedImageFileName,
            ReplacedByOldFileName = replacedByOldFileName,
            ImageUrl = remote.RealIconUrl,
        };
    }

    /// <summary>
    /// 判定图片结论。downloadedSha256 为 null 表示尚未下载（待复核）；
    /// sha 一致即图片无变化；sha 不同且存在复用证据（createTime 变化、品牌变化、
    /// 名称指向不同产品）时按 ID 复用并给出 .old 链改名目标，其余视为同产品换图。
    /// </summary>
    public static ImageOutcome EvaluateImageChange(
        string? localImageSha256,
        string? downloadedSha256,
        NameChangeKind nameKind,
        bool brandChanged,
        bool createTimeChanged,
        IReadOnlyList<string> existingOldImageFiles,
        string? localImageFileName,
        out string? replacedByOldFileName)
    {
        ArgumentNullException.ThrowIfNull(existingOldImageFiles);

        replacedByOldFileName = null;
        if (downloadedSha256 is null)
        {
            return ImageOutcome.PendingReview;
        }

        if (string.Equals(localImageSha256, downloadedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return ImageOutcome.None;
        }

        if (string.IsNullOrEmpty(localImageSha256))
        {
            // 本地无图（或未计算过指纹）而官网有图：直接补图，不涉及旧图改名。
            return ImageOutcome.ImageChanged;
        }

        var reusedByDifferentDevice = createTimeChanged
            || brandChanged
            || nameKind == NameChangeKind.DifferentProduct;
        if (!reusedByDifferentDevice)
        {
            return ImageOutcome.ImageChanged;
        }

        if (localImageFileName is not null)
        {
            replacedByOldFileName = FileNameRules.GetOldImageFileName(
                localImageFileName,
                FileNameRules.GetNextOldGeneration(existingOldImageFiles));
        }

        return ImageOutcome.IdReused;
    }

    /// <summary>
    /// 汇总单型号最高优先级变更类型。同产品线改名（可能同时换图）以改名为准；
    /// 名称未变的图片更换以换图为准；完全无变化返回 null。
    /// </summary>
    private static ChangeType? ResolvePrimaryChangeType(
        NameChangeKind nameKind,
        bool brandChanged,
        bool categoryChanged,
        ImageOutcome imageOutcome,
        bool isAvailable)
    {
        if (imageOutcome == ImageOutcome.IdReused)
        {
            return ChangeType.IdReused;
        }

        if (nameKind == NameChangeKind.SameLine)
        {
            return ChangeType.NameChanged;
        }

        if (imageOutcome == ImageOutcome.ImageChanged)
        {
            return ChangeType.ImageChanged;
        }

        if (nameKind != NameChangeKind.None || brandChanged)
        {
            return ChangeType.NameChanged;
        }

        if (categoryChanged)
        {
            return ChangeType.CategoryChanged;
        }

        if (!isAvailable)
        {
            return ChangeType.Relisted;
        }

        return imageOutcome == ImageOutcome.PendingReview ? ChangeType.None : null;
    }

    private static ProductChange NewChange(RemoteProductState remote) => new()
    {
        Type = ChangeType.New,
        Model = remote.Model,
        NewCategory = remote.Category,
        NewName = remote.Name,
        NewBrand = remote.Brand,
        ImageUrl = remote.RealIconUrl,
    };

    private static ProductChange DelistedChange(LocalProductState local) => new()
    {
        Type = ChangeType.Delisted,
        Model = local.Model,
        OldCategory = local.Category,
        OldName = local.Name,
        OldBrand = local.Brand,
        ImageFileName = local.ImageFileName,
    };
}
