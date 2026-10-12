using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Components.UI.Folders;

public sealed class AvaloniaFileSaver : IFileSaver
{
    public async ValueTask<Result<string, FileSaveError>> SaveAsync(string title, string suggestedName, string content, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return FileSaveError.Unavailable;
        }

        return await await Dispatcher.UIThread.InvokeAsync(
            () => SaveInAsync(window.StorageProvider, title, suggestedName, content, cancellationToken),
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static async Task<Result<string, FileSaveError>> SaveInAsync(IStorageProvider storage, string title, string suggestedName, string content, CancellationToken cancellationToken)
    {
        if (!storage.CanSave)
        {
            return FileSaveError.Unavailable;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
            ShowOverwritePrompt = true,
        });

        if (file is null)
        {
            return FileSaveError.Cancelled;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content.AsMemory(), cancellationToken);

            return file.TryGetLocalPath() ?? file.Name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileSaveError.Unwritable;
        }
    }
}
