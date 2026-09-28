namespace MijiaProductGallery.Core.Interfaces;

/// <summary>应用设置仓储（用户数据，值以 JSON 序列化存储）。</summary>
public interface ISettingsRepository
{
    /// <summary>读取设置；键不存在或反序列化失败时返回默认值。</summary>
    Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        where T : notnull;

    Task SetValueAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : notnull;
}
