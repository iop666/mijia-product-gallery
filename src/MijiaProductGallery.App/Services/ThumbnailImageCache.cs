using Microsoft.UI.Xaml.Media.Imaging;

namespace MijiaProductGallery.App.Services;

/// <summary>
/// 缩略图 BitmapImage LRU 缓存：卡片被 ItemsRepeater 回收后再次实化时复用已创建的图像实例，
/// 消除快速滚动中对同一缩略图的重复磁盘 IO 与重复解码（解码本身仍按需进行，但同路径只做一次）。
/// 容量上限防止大图库常驻内存。
/// </summary>
public sealed class ThumbnailImageCache
{
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> map;
    private readonly LinkedList<Entry> lru = new();
    private readonly int capacity;

    private sealed record Entry(string Path, BitmapImage Image);

    public ThumbnailImageCache(int capacity = 240)
    {
        this.capacity = capacity;
        map = new Dictionary<string, LinkedListNode<Entry>>(capacity, StringComparer.Ordinal);
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return map.Count;
            }
        }
    }

    public BitmapImage? Get(string path)
    {
        lock (gate)
        {
            if (!map.TryGetValue(path, out var node))
            {
                return null;
            }

            lru.Remove(node);
            lru.AddFirst(node);
            return node.Value.Image;
        }
    }

    public BitmapImage GetOrAdd(string path, Func<string, BitmapImage> factory)
    {
        lock (gate)
        {
            if (map.TryGetValue(path, out var node))
            {
                lru.Remove(node);
                lru.AddFirst(node);
                return node.Value.Image;
            }

            var image = factory(path);
            var created = new LinkedListNode<Entry>(new Entry(path, image));
            lru.AddFirst(created);
            map[path] = created;
            while (map.Count > capacity)
            {
                var oldest = lru.Last!;
                lru.RemoveLast();
                map.Remove(oldest.Value.Path);
            }

            return image;
        }
    }
}
