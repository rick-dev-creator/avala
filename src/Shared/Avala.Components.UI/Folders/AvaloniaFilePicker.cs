using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Components.UI.Folders;

public sealed class AvaloniaFilePicker : IFilePicker
{
    public async ValueTask<IReadOnlyList<string>> PickFilesAsync(string title, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return [];
        }

        return await await Dispatcher.UIThread.InvokeAsync(
            () => PickInAsync(window.StorageProvider, title),
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> PickInAsync(IStorageProvider storage, string title)
    {
        if (!storage.CanOpen)
        {
            return [];
        }

        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = true });

        return [.. picked.Select(file => file.TryGetLocalPath()).OfType<string>()];
    }
}
