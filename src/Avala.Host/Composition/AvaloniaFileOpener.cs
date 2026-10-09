using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal sealed class AvaloniaFileOpener : IFileOpener
{
    public async ValueTask<Result<string, FileOpenError>> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return FileOpenError.NotFound;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return FileOpenError.Unavailable;
        }

        var launched = await await Dispatcher.UIThread.InvokeAsync(
            () => window.Launcher.LaunchFileInfoAsync(new FileInfo(path)),
            DispatcherPriority.Normal,
            cancellationToken);

        return launched ? path : FileOpenError.Refused;
    }
}
