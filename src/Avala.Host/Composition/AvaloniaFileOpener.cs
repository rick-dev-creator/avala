using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal sealed class AvaloniaFileOpener : IFileOpener
{
    public async ValueTask<Result<OpenedFile, FileOpenError>> OpenAsync(string path, string template, CancellationToken cancellationToken)
    {
        var created = await CreatedAsync(path, template, cancellationToken);

        if (!created.TryGetValue(out var isNew, out var error))
        {
            return error;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return FileOpenError.Unavailable;
        }

        var launched = await await Dispatcher.UIThread.InvokeAsync(
            () => window.Launcher.LaunchFileInfoAsync(new FileInfo(path)),
            DispatcherPriority.Normal,
            cancellationToken);

        return launched ? new OpenedFile(path, isNew) : FileOpenError.Refused;
    }

    public async ValueTask<Result<OpenedFile, FileOpenError>> OpenFolderAsync(string path, CancellationToken cancellationToken)
    {
        var created = !Directory.Exists(path);

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileOpenError.Uncreatable;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return FileOpenError.Unavailable;
        }

        var launched = await await Dispatcher.UIThread.InvokeAsync(
            () => window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)),
            DispatcherPriority.Normal,
            cancellationToken);

        return launched ? new OpenedFile(path, created) : FileOpenError.Refused;
    }

    private static async Task<Result<bool, FileOpenError>> CreatedAsync(string path, string template, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(file);
            await writer.WriteAsync(template.AsMemory(), cancellationToken);

            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileOpenError.Uncreatable;
        }
    }
}
