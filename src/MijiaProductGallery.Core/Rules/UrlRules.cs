using System.Net;

namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 官网图片 URL 规则：HTML 实体解码、非 ASCII 路径编码与下载域白名单。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static class UrlRules
{
    /// <summary>允许下载图片的域名后缀（米家百科与其图片 CDN）。</summary>
    private static readonly string[] AllowedHostSuffixes =
    [
        "home.mi.com",
        ".mi-img.com",
    ];

    /// <summary>判断 URL 是否属于允许下载的官方域。</summary>
    public static bool IsAllowedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host;
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 规范化官网图片 URL：解码 HTML 实体（接口常返回 &amp;amp; 而非 &amp;），
    /// 并对路径中的非 ASCII 字符做百分号编码；查询串保持原样。
    /// </summary>
    public static bool TryNormalizeImageUrl(string? rawUrl, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var decoded = WebUtility.HtmlDecode(rawUrl.Trim());
        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // AbsoluteUri 会对路径中的非 ASCII 字符按 UTF-8 百分号编码，查询串原样保留。
        normalized = uri.AbsoluteUri;
        return true;
    }

    /// <summary>规范化官网图片 URL；不合法时抛出 FormatException。</summary>
    public static string NormalizeImageUrl(string rawUrl)
    {
        if (!TryNormalizeImageUrl(rawUrl, out var normalized))
        {
            throw new FormatException($"官网图片 URL 不合法：{rawUrl}");
        }

        return normalized;
    }
}
