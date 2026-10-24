using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.ViewModels;

/// <summary>
/// 缩略图加载队列：卡片进入视口时入队，后台并发解码/生成（复用 ThumbnailService 的磁盘缓存），
/// 完成后经 UI 调度器回填卡片。同内容请求合并，失败最多重试若干次，避免缺失原图被反复解码。
/// </summary>
public sealed partial class ThumbnailLoadQueue : ObservableObject
{
    private readonly IThumbnailService thumbnails;
    private readonly IUiDispatcher uiDispatcher;
    private readonly Channel<string> channel;
    private readonly object gate = new();
    private readonly Dictionary<string, List<ProductCard>> waiters = new(StringComparer.Ordinal);
    private int completedCount;

    public ThumbnailLoadQueue(IThumbnailService thumbnails, IUiDispatcher uiDispatcher, int concurrency = 4)
    {
        this.thumbnails = thumbnails;
        this.uiDispatcher = uiDispatcher;
        Concurrency = concurrency;
        channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
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

    /// <summary>请求加载一张卡片；同一图片的并发请求合并为一次解码。</summary>
    public void Request(ProductCard card)
    {
        if (!card.HasImage || card.LoadRequested || card.LoadKey is null || card.LoadAttempts >= ProductCard.MaxLoadAttempts)
        {
            return;
        }

        var key = card.LoadKey;
        card.LoadRequested = true;
        card.LoadAttempts++;
        card.SetLoading();
        PendingCount++;

        lock (gate)
        {
            if (waiters.TryGetValue(key, out var list))
            {
                list.Add(card);
                return;
            }

            waiters[key] = [card];
        }

        channel.Writer.TryWrite(key);
    }

    /// <summary>清除请求标记，用于失败后的手动重试。</summary>
    public void ResetForRetry(ProductCard card)
    {
        card.LoadRequested = false;
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var key in channel.Reader.ReadAllAsync())
        {
            string? path = null;
            try
            {
                var (fileName, sha) = SplitKey(key);
                path = await thumbnails.EnsureThumbnailAsync(fileName, sha);
            }
            catch
            {
                // 失败由卡片占位显示；解码异常不终止后台工作线程。
            }

            uiDispatcher.Post(() => Complete(key, path));
        }
    }

    private void Complete(string key, string? path)
    {
        List<ProductCard> cards;
        lock (gate)
        {
            if (!waiters.Remove(key, out var list))
            {
                return;
            }

            cards = list;
        }

        foreach (var card in cards)
        {
            Interlocked.Increment(ref completedCount);
            PendingCount = Math.Max(0, PendingCount - 1);
            if (path is not null)
            {
                card.SetLoaded(path);
            }
            else
            {
                card.SetFailed();
                ResetForRetry(card);
            }
        }
    }

    private static (string FileName, string Sha) SplitKey(string key)
    {
        var separator = key.LastIndexOf('|');
        return (key[..separator], key[(separator + 1)..]);
    }
}
