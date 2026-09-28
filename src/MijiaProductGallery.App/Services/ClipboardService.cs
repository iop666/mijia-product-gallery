using System.Runtime.InteropServices;
using MijiaProductGallery.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MijiaProductGallery.App.Services;

/// <summary>Windows 剪贴板实现：图片写入位图+文件路径双格式（Explorer/图像软件均可粘贴）。</summary>
public sealed class ClipboardService : ISystemClipboard
{
    public async Task<bool> SetImageFileAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(absolutePath);
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy,
            };
            package.SetStorageItems(new[] { file });
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            package.Properties.Title = file.Name;
            Clipboard.SetContent(package);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    public async Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy,
            };
            package.SetText(text);
            await Task.CompletedTask;
            Clipboard.SetContent(package);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            return false;
        }
    }
}
