using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;

namespace Avala.Workbench.Presenting;

internal sealed class BoardFeed(JobBoard board, IUiDispatcher ui) : IPresentation, IDisposable
{
    private CancellationTokenSource? active;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public ImmutableDictionary<JobId, BoardJob> Jobs => board.Jobs;

    public Task Following { get; private set; } = Task.CompletedTask;

    public bool IsActive => active is not null;

    public Option<BoardJob> Find(JobId job) => board.Find(job);

    public void Start(Func<ImmutableDictionary<JobId, BoardJob>, IReadOnlyList<Func<CancellationToken, Task>>> show)
    {
        if (active is null)
        {
            active = new CancellationTokenSource();
            Following = FollowAsync(show, active.Token);
        }
    }

    public void Stop()
    {
        active?.Cancel();
        active?.Dispose();
        active = null;
    }

    public void Dispose() => Stop();

    public Task LoadAsync<T>(Func<CancellationToken, Task<T>> read, Action<T> apply) =>
        RunAsync(async following =>
        {
            var state = await read(following);
            await ui.InvokeAsync(() => ApplyWhileFollowing(state, apply, following), following);
        });

    public Task ShowAgainAsync(Action show) =>
        RunAsync(following => ui.InvokeAsync(
            () =>
            {
                if (!following.IsCancellationRequested)
                {
                    show();
                    Revision++;
                    Presented?.Invoke(this, new Presented(Revision));
                }
            },
            following).AsTask());

    public Task RunAsync(Func<CancellationToken, Task> load) =>
        active is { } following ? GuardedAsync(load, following.Token) : Task.CompletedTask;

    private static async Task GuardedAsync(Func<CancellationToken, Task> load, CancellationToken following)
    {
        try
        {
            await load(following);
        }
        catch (OperationCanceledException) when (following.IsCancellationRequested)
        {
        }
    }

    private async Task FollowAsync(
        Func<ImmutableDictionary<JobId, BoardJob>, IReadOnlyList<Func<CancellationToken, Task>>> show,
        CancellationToken following)
    {
        try
        {
            await foreach (var _ in board.ChangesAsync(following))
            {
                IReadOnlyList<Func<CancellationToken, Task>> loads = [];
                await ui.InvokeAsync(() => loads = ShowWhileFollowing(show, following), following);

                foreach (var load in loads)
                {
                    await load(following);
                }
            }
        }
        catch (OperationCanceledException) when (following.IsCancellationRequested)
        {
        }
    }

    private IReadOnlyList<Func<CancellationToken, Task>> ShowWhileFollowing(
        Func<ImmutableDictionary<JobId, BoardJob>, IReadOnlyList<Func<CancellationToken, Task>>> show,
        CancellationToken following)
    {
        if (following.IsCancellationRequested)
        {
            return [];
        }

        var loads = show(board.Jobs);
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));

        return loads;
    }

    private static void ApplyWhileFollowing<T>(T state, Action<T> apply, CancellationToken following)
    {
        if (!following.IsCancellationRequested)
        {
            apply(state);
        }
    }
}
