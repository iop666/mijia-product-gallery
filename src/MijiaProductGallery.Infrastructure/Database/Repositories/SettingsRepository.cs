using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>应用设置仓储（用户数据，JSON 值）。应用内每次读写独立上下文，避免与查询并发冲突。</summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public SettingsRepository(GalleryDbContext context) => shared = context;

    public SettingsRepository(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    public async Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        where T : notnull
    {
        await using var scope = Rent();
        var entry = await scope.Context.AppSettings.AsNoTracking()
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
        await using var scope = Rent();
        var context = scope.Context;
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
