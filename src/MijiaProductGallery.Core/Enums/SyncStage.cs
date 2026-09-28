namespace MijiaProductGallery.Core.Enums;

/// <summary>同步流程阶段，用于同步中心展示与失败定位。</summary>
public enum SyncStage
{
    NotStarted,
    FetchingCategories,
    FetchingProducts,
    Comparing,
    DownloadingImages,
    GeneratingThumbnails,
    UpdatingDatabase,
    Completed,
}
