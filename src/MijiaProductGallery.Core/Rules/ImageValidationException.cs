using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Rules;

/// <summary>图片格式校验异常。Reason 为面向用户与日志的原因说明。</summary>
public sealed class ImageValidationException(string reason) : InvalidOperationException(reason);
