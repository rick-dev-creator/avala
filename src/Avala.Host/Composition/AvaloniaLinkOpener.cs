using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal sealed class AvaloniaLinkOpener : ILinkOpener
{
    public async ValueTask<Result<Uri, FileOpenError>> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return FileOpenError.Unavailable;
        }

        var launched = await await Dispatcher.UIThread.InvokeAsync(
            () => window.Launcher.LaunchUriAsync(link),
            DispatcherPriority.Normal,
            cancellationToken);

        return launched ? link : FileOpenError.Refused;
    }
}
