using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;

namespace MijiaProductGallery.Infrastructure.Sync;

/// <summary>
/// 使用行为记录服务：把查看/复制/拖拽落盘为计数与事件。
/// 每次记录经 DbContextFactory 使用独立上下文（并发安全，事务原子，不阻塞 UI 调用方）；
/// 是否计入由行为开关设置决定（AppSettingsKeys.RecordViews/RecordCopies/RecordDrags，默认全部计入）。
/// </summary>
public sealed class UsageService(IDbContextFactory<GalleryDbContext> contextFactory) : IUsageService
{
    // 桌面场景下使用记录为毫秒级小事务：进程内串行化即可保证原子与线程安全，
    // 同时避免并发写同一 SQLite 文件时的锁冲突。
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public async Task RecordAsync(int productId, UsageType type, long occurredUnix, CancellationToken cancellationToken = default)
    {
        if (!await IsRecordingEnabledAsync(type, cancellationToken))
        {
            return;
        }

        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var repository = new UsageRepository(context);
            await repository.RecordAsync(productId, type, occurredUnix, cancellationToken);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private async Task<bool> IsRecordingEnabledAsync(UsageType type, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var settingsRepository = new SettingsRepository(context);
        return type switch
        {
            UsageType.View => await settingsRepository.GetValueAsync(AppSettingsKeys.RecordViews, true, cancellationToken),
            UsageType.Copy => await settingsRepository.GetValueAsync(AppSettingsKeys.RecordCopies, true, cancellationToken),
            UsageType.Drag => await settingsRepository.GetValueAsync(AppSettingsKeys.RecordDrags, true, cancellationToken),
            _ => true,
        };
    }
}
