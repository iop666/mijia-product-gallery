using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>
/// 产品官方数据镜像（来源：米家百科）。
/// 只承载官方字段；收藏、合集、使用统计、历史、搜索历史等用户状态由独立模型承载，同步流程不得写入。
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    /// <summary>型号，即示例样图文件名主名（如 zhimi.heater.za1），全库唯一。</summary>
    public required string Model { get; set; }

    public required string Name { get; set; }

    public required string Brand { get; set; }

    /// <summary>归属大类名；一个型号全库只属一个大类。</summary>
    public required string Category { get; set; }

    /// <summary>小类，预留（接口暂无此数据）。</summary>
    public string? SubCategory { get; set; }

    /// <summary>官网当前图片的原始文件名（型号.扩展名）。</summary>
    public string? ImageFileName { get; set; }

    /// <summary>图片在本地图库目录中的相对路径（可能与原始文件名不同，如保留名安全映射）。</summary>
    public string? ImagePath { get; set; }

    /// <summary>官网图片 URL（最近一次同步见到的地址）。</summary>
    public string? ImageUrl { get; set; }

    public ImageFormat ImageFormat { get; set; } = ImageFormat.Unknown;

    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public long FileSize { get; set; }

    /// <summary>现用图片内容的 SHA-256（十六进制小写）。</summary>
    public string? Sha256 { get; set; }

    /// <summary>官网在架状态；官网移除时置 false，记录与图片保留。</summary>
    public bool IsAvailable { get; set; }

    /// <summary>官网创建时间（Unix 秒）。</summary>
    public long? CreateTimeUnix { get; set; }

    /// <summary>官网更新时间（Unix 秒）。</summary>
    public long? UpdateTimeUnix { get; set; }

    /// <summary>本地首次收录时间（Unix 秒），用于"最新添加"排序。</summary>
    public long FirstSeenUnix { get; set; }

    /// <summary>最近一次在官网见到的时间（Unix 秒）。</summary>
    public long LastSeenUnix { get; set; }

    /// <summary>
    /// 随机浏览游标键（种子导入/同步新增时生成一次，终身不变；null 待启动回填）。
    /// 非官方数据、非用户数据：纯本地派生键，同步不读取也不覆盖。
    /// </summary>
    public long? RandomKey { get; set; }
}
