using Avala.Runtime.Diagnostics;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal sealed class OrderlyExit(CompositionRoot composition, DataFolderClaim claim, LogFile log, CrashLog crashes)
{
    public void Hold()
    {
        using var stopped = new CancellationTokenSource();
        _ = StopAsync(stopped);
        Dispatcher.UIThread.MainLoop(stopped.Token);
    }

    private async Task StopAsync(CancellationTokenSource stopped)
    {
        try
        {
            await composition.DisposeAsync();
            crashes.Stopped();
        }
        finally
        {
            crashes.Unwatch();
            await log.DisposeAsync();
            claim.Dispose();
            await stopped.CancelAsync();
        }
    }
}
