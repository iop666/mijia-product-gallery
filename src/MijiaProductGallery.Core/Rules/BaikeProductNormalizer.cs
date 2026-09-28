using System.Text.RegularExpressions;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 米家百科产品数据标准化：空白清理与型号格式校验，产出对比规则可用的官网侧快照。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static partial class BaikeProductNormalizer
{
    /// <summary>型号字符白名单：字母、数字、点、下划线、连字符。</summary>
    public static Regex ValidModelPattern { get; } = ModelPattern();

    /// <summary>校验型号是否符合字符白名单且非空。</summary>
    public static bool IsValidModel(string model)
    {
        return !string.IsNullOrWhiteSpace(model) && ValidModelPattern.IsMatch(model);
    }

    /// <summary>标准化接口产品对象；category 为按归类规则解析出的归属大类名。</summary>
    public static RemoteProductState Normalize(BaikeProductDto dto, string category)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var model = dto.Model.Trim();
        if (!IsValidModel(model))
        {
            throw new FormatException($"接口返回的型号不合法：'{dto.Model}'");
        }

        return new RemoteProductState
        {
            Model = model,
            Name = dto.Name.Trim(),
            Brand = dto.Brand.Trim(),
            Category = category,
            RealIconUrl = dto.RealIcon,
            CreateTimeUnix = dto.CreateTimeUnix,
            UpdateTimeUnix = dto.UpdateTimeUnix,
        };
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
}
