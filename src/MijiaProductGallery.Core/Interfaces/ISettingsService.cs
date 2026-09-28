namespace MijiaProductGallery.Core.Interfaces;

/// <summary>应用设置读写契约（由数据层实现，值为 JSON 序列化）。</summary>
public interface ISettingsService
{
    /// <summary>读取设置值；不存在或反序列化失败时返回默认值。</summary>
    T GetValue<T>(string key, T defaultValue) where T : notnull;

    /// <summary>写入设置值。</summary>
    void SetValue<T>(string key, T value) where T : notnull;
}
