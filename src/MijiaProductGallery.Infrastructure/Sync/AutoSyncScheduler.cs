using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;

namespace MijiaProductGallery.Infrastructure.Sync;

/// <summary>
/// 自动同步调度器：周期检查 SyncAutoInterval 是否到期，到期触发 Scheduled 同步。
/// Off 恒不触发；Startup 仅应用启动后的首次检查触发；周期检查每 15 分钟一次。
/// </summary>
public sealed class AutoSyncScheduler : IAutoSyncScheduler, IDisposable
{
    private const string SettingsKey = "Sync.AutoInterval";
    private static readonly TimeSpan CheckPeriod = TimeSpan.FromMinutes(15);

    private readonly ISyncService syncService;
    private readonly IDbContextFactory<GalleryDbContext> contextFactory;
    private readonly TimeProvider time;
    private readonly ILogger<AutoSyncScheduler>? logger;
    private Timer? timer;
    private int checking;

    public AutoSyncScheduler(
        ISyncService syncService,
        IDbContextFactory<GalleryDbContext> contextFactory,
        TimeProvider? timeProvider = null,
        ILogger<AutoSyncScheduler>? logger = null)
    {
        this.syncService = syncService;
        this.contextFactory = contextFactory;
        time = timeProvider ?? TimeProvider.System;
        this.logger = logger;
    }

    /// <summary>启动周期检查（幂等）。</summary>
    public void Start()
    {
        if (timer is not null)
        {
            return;
        }

        timer = new Timer(
            async _ =>
            {
                try
                {
                    await CheckAndTriggerAsync(isStartupCheck: false);
                }
                catch (Exception exception)
                {
                    logger?.LogWarning(exception, "自动同步周期检查失败");
                }
            },
            null,
            CheckPeriod,
            CheckPeriod);
    }

    /// <summary>执行一次到期检查：到期触发 Scheduled 同步。</summary>
    public async Task CheckAndTriggerAsync(bool isStartupCheck = false, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref checking, 1) != 0)
        {
            return;
        }

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var settingsRepository = new SettingsRepository(context);
            var syncStateRepository = new SyncStateRepository(context);

            var intervalText = await settingsRepository.GetValueAsync(
                SettingsKey,
                nameof(SyncAutoInterval.Daily),
                cancellationToken);
            var interval = Enum.TryParse<SyncAutoInterval>(intervalText, ignoreCase: true, out var parsed)
                ? parsed
                : SyncAutoInterval.Daily;

            var state = await syncStateRepository.GetStateAsync(cancellationToken);
            var nowUnix = time.GetUtcNow().ToUnixTimeSeconds();
            if (!IsDue(interval, state.LastSuccessfulSyncUnix, nowUnix, isStartupCheck))
            {
                return;
            }

            logger?.LogInformation("自动同步到期（{Interval}），触发 Scheduled 同步", interval);
            await syncService.SyncNowAsync(SyncTrigger.Scheduled, cancellationToken);
        }
        finally
        {
            checking = 0;
        }
    }

    /// <summary>
    /// 到期判定（纯函数）：Off 恒否；Startup 仅启动检查；其余按距上次成功同步的间隔。
    /// 从未成功同步（null）视为到期。
    /// </summary>
    public static bool IsDue(SyncAutoInterval interval, long? lastSuccessfulSyncUnix, long nowUnix, bool isStartupCheck = false)
    {
        return interval switch
        {
            SyncAutoInterval.Off => false,
            SyncAutoInterval.Startup => isStartupCheck,
            SyncAutoInterval.Hours6 => Elapsed(lastSuccessfulSyncUnix, nowUnix, 6 * 3600),
            SyncAutoInterval.Daily => Elapsed(lastSuccessfulSyncUnix, nowUnix, 24 * 3600),
            SyncAutoInterval.Weekly => Elapsed(lastSuccessfulSyncUnix, nowUnix, 7 * 24 * 3600),
            _ => false,
        };
    }

    private static bool Elapsed(long? lastSuccessfulSyncUnix, long nowUnix, long intervalSeconds)
    {
        return lastSuccessfulSyncUnix is null || nowUnix - lastSuccessfulSyncUnix.Value >= intervalSeconds;
    }

    public void Dispose()
    {
        timer?.Dispose();
        timer = null;
    }
}
