using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>冻结基准（2026-09-28 快照）中的一行清单记录。</summary>
public sealed record BaselineRow
{
    [Name("大类")]
    public string Category { get; set; } = string.Empty;

    [Name("品牌")]
    public string Brand { get; set; } = string.Empty;

    [Name("产品名称")]
    public string Name { get; set; } = string.Empty;

    [Name("型号")]
    public string Model { get; set; } = string.Empty;

    [Name("图片文件名")]
    public string ImageFileName { get; set; } = string.Empty;

    [Name("备注")]
    public string Remark { get; set; } = string.Empty;
}

/// <summary>
/// 冻结基准目录：把 2026-09-28 快照清单转换为对比规则的两侧输入。
/// 官网侧只含在架型号；下架型号仅存在于本地侧。
/// </summary>
public static class BaselineCatalog
{
    public const string DelistedRemark = "官网已移除该型号";

    public static readonly string[] ExpectedCategories =
    [
        "个护与起居", "传感器", "其他", "出行车载", "办公学习", "卫浴", "厨房电器",
        "安防", "宠物与植物", "影音娱乐", "插座开关", "新上线", "清洁电器", "照明",
        "环境电器", "路由网关", "运动健康",
    ];

    private static readonly Lazy<IReadOnlyList<BaselineRow>> RowsLazy = new(LoadRows);

    public static IReadOnlyList<BaselineRow> Rows => RowsLazy.Value;

    /// <summary>本地侧输入：全部型号（含下架保留）。</summary>
    public static IReadOnlyList<LocalProductState> ToLocalStates(IEnumerable<BaselineRow> rows)
    {
        return rows.Select(row => new LocalProductState
        {
            Model = row.Model,
            Name = row.Name,
            Brand = row.Brand,
            Category = row.Category,
            ImageFileName = string.IsNullOrEmpty(row.ImageFileName) ? null : row.ImageFileName,
            ImageSha256 = null,
            IsAvailable = !string.Equals(row.Remark, DelistedRemark, StringComparison.Ordinal),
            CreateTimeUnix = null,
            UpdateTimeUnix = null,
        }).ToList();
    }

    /// <summary>官网侧输入：仅含在架型号。</summary>
    public static IReadOnlyList<RemoteProductState> ToRemoteStates(IEnumerable<BaselineRow> rows)
    {
        return rows
            .Where(row => !string.Equals(row.Remark, DelistedRemark, StringComparison.Ordinal))
            .Select(row => new RemoteProductState
            {
                Model = row.Model,
                Name = row.Name,
                Brand = row.Brand,
                Category = row.Category,
                RealIconUrl = $"https://cdn.cnbj1.fds.api.mi-img.com/baseline/{row.Model}.png",
                CreateTimeUnix = 0,
                UpdateTimeUnix = 0,
            }).ToList();
    }

    private static IReadOnlyList<BaselineRow> LoadRows()
    {
        using var reader = new StreamReader(
            Path.Combine(TestPaths.FixturesRoot, "baseline-2026-09-28", "00_总清单.csv"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return [.. csv.GetRecords<BaselineRow>()];
    }
}
