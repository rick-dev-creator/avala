using Avala.Runtime.Diagnostics;
using Avala.Sdk.Processes;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal sealed class OrderlyExit(CompositionRoot composition, DataFolderClaim claim, LogFile log, CrashLog crashes)
{
    private bool holding;

    public void Follow(IClassicDesktopStyleApplicationLifetime desktop, Action leaving)
    {
        desktop.ShutdownRequested += (_, request) => request.Cancel |= holding;
        desktop.Exit += (_, _) =>
        {
            if (!holding)
            {
                leaving();
                Hold();
            }
        };
    }

    private void Hold()
    {
        holding = true;
        var frame = new DispatcherFrame(exitWhenRequested: false);
        _ = StopAsync(frame);
        Dispatcher.UIThread.PushFrame(frame);
    }

    private async Task StopAsync(DispatcherFrame frame)
    {
        try
        {
            var bounded = new BoundedStop(composition.Services.GetRequiredService<IProcessTrees>(), TimeProvider.System);

            if (await bounded.StopAsync(() => composition.DisposeAsync().AsTask()))
            {
                crashes.Stopped();
            }
            else
            {
                crashes.CutShort();
            }
        }
        finally
        {
            crashes.Unwatch();
            await log.DisposeAsync();
            claim.Dispose();
            frame.Continue = false;
        }
    }
}
