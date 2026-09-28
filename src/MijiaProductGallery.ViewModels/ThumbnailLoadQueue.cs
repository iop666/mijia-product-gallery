using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.ViewModels;

/// <summary>
/// 缩略图加载队列：卡片进入视口时入队，后台并发解码/生成（复用 ThumbnailService 的磁盘缓存），
/// 完成后经 UI 调度器回填卡片。重复请求（同卡片/同内容）自动去重。
/// </summary>
public sealed partial class ThumbnailLoadQueue : ObservableObject
{
    private readonly IThumbnailService thumbnails;
    private readonly IUiDispatcher uiDispatcher;
    private readonly Channel<ProductCard> channel;
    private readonly HashSet<string> inFlight = new(StringComparer.Ordinal);
    private int completedCount;

    public ThumbnailLoadQueue(IThumbnailService thumbnails, IUiDispatcher uiDispatcher, int concurrency = 4)
    {
        this.thumbnails = thumbnails;
        this.uiDispatcher = uiDispatcher;
        Concurrency = concurrency;
        channel = Channel.CreateUnbounded<ProductCard>(new UnboundedChannelOptions
        {
            SingleReader = false,
        });
        for (var i = 0; i < concurrency; i++)
        {
            _ = Task.Run(WorkerLoopAsync);
        }
    }

    /// <summary>并发加载数。</summary>
    public int Concurrency { get; }

    /// <summary>累计完成数（含失败），供诊断。</summary>
    public int CompletedCount => completedCount;

    [ObservableProperty]
    private int pendingCount;

    /// <summary>请求加载一张卡片；同卡片重复请求与失败重试均安全。</summary>
    public void Request(ProductCard card)
    {
        if (!card.HasImage || card.LoadRequested || card.LoadKey is null)
        {
            return;
        }

        card.LoadRequested = true;
        card.SetLoading();
        PendingCount++;
        channel.Writer.TryWrite(card);
    }

    /// <summary>清除请求标记，用于失败后的手动重试。</summary>
    public void ResetForRetry(ProductCard card)
    {
        card.LoadRequested = false;
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var card in channel.Reader.ReadAllAsync())
        {
            try
            {
                var key = card.LoadKey!;
                var fileName = card.ImageFileName!;
                var sha = card.Sha256!;
                var path = await thumbnails.EnsureThumbnailAsync(fileName, sha);
                uiDispatcher.Post(() =>
                {
                    lock (inFlight)
                    {
                        inFlight.Remove(key);
                    }

                    Interlocked.Increment(ref completedCount);
                    PendingCount--;
                    card.SetLoaded(path);
                });
            }
            catch
            {
                uiDispatcher.Post(() =>
                {
                    card.SetFailed();
                    ResetForRetry(card);
                    Interlocked.Increment(ref completedCount);
                    PendingCount--;
                });
            }
        }
    }
}
