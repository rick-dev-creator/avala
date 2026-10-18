using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Components.UI.Copying;

public sealed class AvaloniaClipboard : Sdk.IClipboard
{
    private readonly Func<TopLevel?> window;

    public AvaloniaClipboard()
        : this(() => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main } ? main : null)
    {
    }

    internal AvaloniaClipboard(Func<TopLevel?> window) => this.window = window;

    public ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken) =>
        OnClipboardAsync(
            async (_, clipboard) =>
            {
                await clipboard.SetTextAsync(text);

                return true;
            },
            cancellationToken);

    public ValueTask<bool> CopyImageAsync(string path, CancellationToken cancellationToken) =>
        OnClipboardAsync(
            async (top, clipboard) =>
            {
                if (await top.StorageProvider.TryGetFileFromPathAsync(path) is not { } file)
                {
                    return false;
                }

                var item = new DataTransferItem();
                item.SetFile(file);
                item.SetBitmap(Decoded(path));
                var transfer = new DataTransfer();
                transfer.Add(item);
                await clipboard.SetDataAsync(transfer);

                return true;
            },
            cancellationToken);

    public ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        OnClipboardAsync(
            async (top, clipboard) =>
            {
                var files = new List<IStorageItem>();

                foreach (var path in paths)
                {
                    if (await top.StorageProvider.TryGetFileFromPathAsync(path) is not { } file)
                    {
                        return false;
                    }

                    files.Add(file);
                }

                if (files.Count == 0)
                {
                    return false;
                }

                await clipboard.SetFilesAsync(files);

                return true;
            },
            cancellationToken);

    public ValueTask<bool> CopyLinkAsync(Uri link, CancellationToken cancellationToken) =>
        link.IsAbsoluteUri ? CopyAsync(link.AbsoluteUri, cancellationToken) : ValueTask.FromResult(false);

    private static Bitmap Decoded(string path)
    {
        using var stream = File.OpenRead(path);

        return new Bitmap(stream);
    }

    private async ValueTask<bool> OnClipboardAsync(Func<TopLevel, IClipboard, Task<bool>> copy, CancellationToken cancellationToken)
    {
        if (Application.Current is null)
        {
            return false;
        }

        return await await Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                if (window() is not { Clipboard: { } clipboard } top)
                {
                    return false;
                }

                try
                {
                    return await copy(top, clipboard);
                }
                catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return false;
                }
            },
            DispatcherPriority.Normal,
            cancellationToken);
    }
}
