using Avala.Sdk;

namespace Avala.Workbench.Following;

internal sealed class LiveFeed(Pulse pulse, IUiDispatcher ui) : IDisposable
{
    private CancellationTokenSource? active;

    public Task Following { get; private set; } = Task.CompletedTask;

    public bool IsActive => active is not null;

    public void Start<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show)
    {
        if (active is null)
        {
            active = new CancellationTokenSource();
            var following = active.Token;
            Following = Task.Run(() => FollowAsync(read, show, following), CancellationToken.None);
        }
    }

    public void Refresh() => pulse.Beat();

    public void Stop()
    {
        active?.Cancel();
        active?.Dispose();
        active = null;
    }

    public void Dispose() => Stop();

    private async Task FollowAsync<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show, CancellationToken following)
    {
        try
        {
            await foreach (var _ in pulse.ChangesAsync(following))
            {
                var state = await read(following);
                await ui.InvokeAsync(() => ShowWhileFollowing(state, show, following), following);
            }
        }
        catch (OperationCanceledException) when (following.IsCancellationRequested)
        {
        }
    }

    private static void ShowWhileFollowing<T>(T state, Action<T> show, CancellationToken following)
    {
        if (!following.IsCancellationRequested)
        {
            show(state);
        }
    }
}
