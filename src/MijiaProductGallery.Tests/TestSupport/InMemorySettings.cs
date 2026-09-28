using System.Collections.Concurrent;
using System.Text.Json;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>内存设置仓储桩：语义与生产实现一致（JSON 序列化，键不存在返回默认值）。</summary>
public sealed class InMemorySettings : ISettingsRepository
{
    private readonly ConcurrentDictionary<string, string> store = new(StringComparer.Ordinal);

    public Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        where T : notnull
    {
        if (!store.TryGetValue(key, out var serialized))
        {
            return Task.FromResult(defaultValue);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(serialized);
            return Task.FromResult(value ?? defaultValue);
        }
        catch (JsonException)
        {
            return Task.FromResult(defaultValue);
        }
    }

    public Task SetValueAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : notnull
    {
        store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }
}
