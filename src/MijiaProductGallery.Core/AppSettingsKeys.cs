namespace MijiaProductGallery.Core;

/// <summary>AppSettings 统一键名（用户设置全部存 AppSettings 表，JSON/字符串值）。</summary>
public static class AppSettingsKeys
{
    // 常规
    public const string Theme = "General.Theme";
    public const string LaunchView = "General.LaunchView";

    // 图库（缩略图运行时参数）
    public const string ThumbMaxEdge = "Library.ThumbMaxEdge";
    public const string ThumbQuality = "Library.ThumbQuality";

    // 常规（首启声明）
    public const string LicenseAgreed = "General.LicenseAgreed";

    // 图库（浏览模式与分页）
    public const string GalleryBrowseMode = "Gallery.BrowseMode";
    public const string GalleryPageSize = "Gallery.PageSize";

    // 行为（使用计数开关）
    public const string RecordViews = "Behavior.RecordViews";
    public const string RecordCopies = "Behavior.RecordCopies";
    public const string RecordDrags = "Behavior.RecordDrags";

    // 同步
    public const string SyncAutoInterval = "Sync.AutoInterval";

    // 默认值
    public const string ThemeDefault = "System";
    public const string LaunchViewDefault = "Gallery";
    public const int ThumbMaxEdgeDefault = 480;
    public const int ThumbQualityDefault = 80;
    public const string GalleryBrowseModeDefault = "Paged";
    public const string GalleryBrowseModeContinuous = "Continuous";
    public const int GalleryPageSizeDefault = 21;
    public const int GalleryPageSizeMin = 9;
    public const int GalleryPageSizeMax = 140;
}
