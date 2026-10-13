using Avala.Sdk;
using Avala.Sdk.Updates;
using Avalonia.Controls.ApplicationLifetimes;

namespace Avala.Host.Composition;

internal static class UpdateRestart
{
    public static async Task FollowAsync(IClassicDesktopStyleApplicationLifetime desktop, IAsyncEnumerable<RestartingToUpdate> restarts, IUiDispatcher ui, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in restarts.WithCancellation(cancellationToken))
            {
                await ui.InvokeAsync(() => desktop.Shutdown(), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
