namespace MijiaProductGallery.Core.Enums;

/// <summary>示例样图的实际格式（按文件头判定，不信任 Content-Type）。</summary>
public enum ImageFormat
{
    Unknown,
    Png,
    Jpg,
    Gif,
    Webp,
}
