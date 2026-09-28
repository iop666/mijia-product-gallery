namespace MijiaProductGallery.Core.Enums;

/// <summary>名称对比结论的细分，用于区分纯换图、改名与换设备。</summary>
public enum NameChangeKind
{
    /// <summary>名称一致。</summary>
    None,

    /// <summary>仅空白、大小写等微调。</summary>
    Trivial,

    /// <summary>同一产品线的名称演进（一方包含另一方，或公共前缀足够长）。</summary>
    SameLine,

    /// <summary>指向明显不同的产品；无法判断时也归入此类（保守处理）。</summary>
    DifferentProduct,
}
