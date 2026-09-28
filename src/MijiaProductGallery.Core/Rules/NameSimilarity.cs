using System.Text;
using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 名称对比规则：把两个产品名称的差异归为无变化 / 微调 / 同产品线演进 / 不同产品。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static class NameSimilarity
{
    /// <summary>判定"同产品线"所需的最短公共前缀长度（按字符）。</summary>
    public const int MinSharedPrefixLength = 4;

    /// <summary>规范化名称：去除首尾空白，内部连续空白折叠为单个空格。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>对比两个名称，产出细分结论。任一侧为空且另一侧非空时按"不同产品"保守处理。</summary>
    public static NameChangeKind Classify(string? oldName, string? newName)
    {
        var oldNormalized = Normalize(oldName);
        var newNormalized = Normalize(newName);

        if (string.Equals(oldNormalized, newNormalized, StringComparison.Ordinal))
        {
            return NameChangeKind.None;
        }

        if (string.Equals(
                StripWhitespace(oldNormalized),
                StripWhitespace(newNormalized),
                StringComparison.OrdinalIgnoreCase))
        {
            return NameChangeKind.Trivial;
        }

        if (oldNormalized.Length == 0 || newNormalized.Length == 0)
        {
            return NameChangeKind.DifferentProduct;
        }

        return IsSameProductLine(oldNormalized, newNormalized)
            ? NameChangeKind.SameLine
            : NameChangeKind.DifferentProduct;
    }

    /// <summary>
    /// 同产品线判定：一方包含另一方；或公共前缀足够长且去除前缀后的余部
    /// 仍互为包含 / 前缀延续 / 为版本号形态（如" 4"、"2K"）。
    /// 仅有"小米智能"这类泛用前缀、余部指向不同品类的名称不算同产品线。
    /// </summary>
    private static bool IsSameProductLine(string left, string right)
    {
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
        {
            return true;
        }

        var prefixLength = CommonPrefixLength(left, right);
        if (prefixLength < MinSharedPrefixLength)
        {
            return false;
        }

        var leftRemainder = left[prefixLength..];
        var rightRemainder = right[prefixLength..];
        if (leftRemainder.Contains(rightRemainder, StringComparison.Ordinal)
            || rightRemainder.Contains(leftRemainder, StringComparison.Ordinal))
        {
            return true;
        }

        if (CommonPrefixLength(leftRemainder, rightRemainder) >= MinSharedPrefixLength)
        {
            return true;
        }

        return IsVersionSuffix(leftRemainder) || IsVersionSuffix(rightRemainder);
    }

    /// <summary>余部是否为短版本号形态：仅含字母数字、空白与括号连接符，且至少含一个数字。</summary>
    private static bool IsVersionSuffix(string value)
    {
        if (value.Length == 0 || value.Length > 8)
        {
            return false;
        }

        var hasDigit = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (!char.IsLetterOrDigit(ch) && ch is not ('.' or '-' or '+' or '(' or ')' or '（' or '）'))
            {
                return false;
            }

            hasDigit |= char.IsDigit(ch);
        }

        return hasDigit;
    }

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return length;
    }
}
