using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>右键菜单文本复制的内容种类。</summary>
public enum CardTextKind
{
    Name,
    Model,
    Brand,
    FullInfo,
}

/// <summary>
/// 卡片动作服务：复制图片/文本/完整信息（经系统剪贴板）与使用计数。
/// 图片复制失败（文件缺失/剪贴板占用）必须返回错误状态，绝不静默。
/// 复制成功一律 CopyCount+1；收藏请求只抛出事件（收藏系统在后续阶段实现）。
/// </summary>
public partial class CardActionService : ObservableObject
{
    private readonly IImageStore imageStore;
    private readonly ISystemClipboard clipboard;
    private readonly IUsageService usage;
    private readonly IFavoriteService favorites;

    public CardActionService(IImageStore imageStore, ISystemClipboard clipboard, IUsageService usage, IFavoriteService favorites)
    {
        this.imageStore = imageStore;
        this.clipboard = clipboard;
        this.usage = usage;
        this.favorites = favorites;
    }

    /// <summary>切换收藏状态（幂等，用户数据）；返回切换后的状态。</summary>
    public Task<bool> ToggleFavoriteAsync(ProductCard card, CancellationToken cancellationToken = default)
    {
        return favorites.ToggleAsync(card.ProductId, cancellationToken);
    }

    /// <summary>收藏请求事件（用户数据，Phase 12 实现持久化）。</summary>
    public event Action<ProductCard>? FavoriteRequested;

    public async Task<CardActionResult> CopyImageAsync(ProductCard card, CancellationToken cancellationToken = default)
    {
        if (TryResolvePath(card, out var message, out var absolutePath))
        {
            if (await clipboard.SetImageFileAsync(absolutePath!, cancellationToken))
            {
                await CountAsync(card, UsageType.Copy, cancellationToken);
                return CardActionResult.Ok();
            }

            return CardActionResult.Fail("剪贴板写入失败（可能被其他程序占用），请重试");
        }

        return CardActionResult.Fail(message);
    }

    public async Task<CardActionResult> CopyTextAsync(ProductCard card, CardTextKind kind, CancellationToken cancellationToken = default)
    {
        var text = BuildText(card, kind);
        if (!await clipboard.SetTextAsync(text, cancellationToken))
        {
            return CardActionResult.Fail("剪贴板写入失败（可能被其他程序占用），请重试");
        }

        await CountAsync(card, UsageType.Copy, cancellationToken);
        return CardActionResult.Ok();
    }

    /// <summary>右键菜单"加入收藏"：当前仅抛出事件，不写入任何存储。</summary>
    public void RequestFavorite(ProductCard card)
    {
        FavoriteRequested?.Invoke(card);
    }

    private bool TryResolvePath(ProductCard card, out string message, out string? absolutePath)
    {
        absolutePath = null;
        if (!card.HasImage || card.ImagePath is null)
        {
            message = "该型号无图片，不能复制或拖拽";
            return false;
        }

        absolutePath = imageStore.ResolveAbsolutePath(card.ImagePath);
        if (absolutePath is null || !File.Exists(absolutePath))
        {
            message = "图片文件缺失，请先完成同步后再试";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string BuildText(ProductCard card, CardTextKind kind)
    {
        return kind switch
        {
            CardTextKind.Name => card.Name,
            CardTextKind.Model => card.Model,
            CardTextKind.Brand => card.Brand,
            CardTextKind.FullInfo => $"名称：{card.Name}\n型号：{card.Model}\n品牌：{card.Brand}\n分类：{card.Category}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private async Task CountAsync(ProductCard card, UsageType type, CancellationToken cancellationToken)
    {
        try
        {
            await usage.RecordAsync(card.ProductId, type, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        }
        catch
        {
            // 计数失败不影响动作本身的结果。
        }
    }
}
