using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>应用设置仓储（用户数据，JSON 值）。</summary>
public sealed class SettingsRepository(GalleryDbContext context) : ISettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var entry = await context.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (entry is null)
        {
            return defaultValue;
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(entry.Value, JsonOptions);
            return value ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task SetValueAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        var entry = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (entry is not null)
        {
            context.AppSettings.Remove(entry);
        }

        context.AppSettings.Add(new AppSettingEntry { Key = key, Value = serialized });
        await context.SaveChangesAsync(cancellationToken);
    }
}
