using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Avala.Components.UI.Copying;

public sealed class AvaloniaClipboard : Sdk.IClipboard
{
    public async ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return false;
        }

        return await await Dispatcher.UIThread.InvokeAsync(
            async () =>
            {
                if (window.Clipboard is not { } clipboard)
                {
                    return false;
                }

                await clipboard.SetTextAsync(text);

                return true;
            },
            DispatcherPriority.Normal,
            cancellationToken);
    }
}
