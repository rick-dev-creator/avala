using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Microsoft.Extensions.Logging;

namespace Avala.Workbench.Presenting;

internal sealed partial class BoardFeed(JobBoard board, IUiDispatcher ui, ILogger<BoardFeed> logger) : IPresentation, IDisposable
{
    private CancellationTokenSource? active;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public ImmutableDictionary<JobId, BoardJob> Jobs => board.Jobs;

    public Task Following { get; private set; } = Task.CompletedTask;

    public bool IsActive => active is not null;

    public Option<BoardJob> Find(JobId job) => board.Find(job);

    public ImmutableDictionary<JobId, BoardContest> Contests => board.Contests;

    public ProjectMap Projects => board.Projects;

    public ImmutableDictionary<JobId, Documentation.Contracts.JobDocumentation> Docs => board.Docs;

    public ImmutableHashSet<JobId> Drafted => board.Drafted;

    public IReadOnlyList<JobCheckpoint> CheckpointsOf(JobId job) => board.Checkpoints.GetValueOrDefault(job, []);

    public Option<Documentation.Contracts.JobDocumentation> DocsOf(JobId job) =>
        board.Docs.TryGetValue(job, out var found) ? found : Option<Documentation.Contracts.JobDocumentation>.None;

    public ImmutableDictionary<JobId, BoardRemote> Remotes => board.Remotes;

    public Option<BoardContest> ContestOf(JobId job) => board.ContestOf(job);

    public int RevisionOf(JobId job) => board.Revision(job);

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
                    Guarded(show);
                    Revision++;
                    Presented?.Invoke(this, new Presented(Revision));
                }
            },
            following).AsTask());

    public Task RunAsync(Func<CancellationToken, Task> load) =>
        active is { } following ? GuardedAsync(load, following.Token) : Task.CompletedTask;

    private async Task GuardedAsync(Func<CancellationToken, Task> load, CancellationToken following)
    {
        try
        {
            await load(following);
        }
        catch (Exception exception)
        {
            if (!following.IsCancellationRequested)
            {
                LogFailed(exception);
            }
        }
    }

    private void Guarded(Action show)
    {
        try
        {
            show();
        }
        catch (Exception exception)
        {
            LogFailed(exception);
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
                    await LoadAsync(load, following);
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

        IReadOnlyList<Func<CancellationToken, Task>> loads = [];

        try
        {
            loads = show(board.Jobs);
        }
        catch (Exception exception)
        {
            LogFailed(exception);
        }

        Revision++;
        Presented?.Invoke(this, new Presented(Revision));

        return loads;
    }

    private async Task LoadAsync(Func<CancellationToken, Task> load, CancellationToken following)
    {
        try
        {
            await load(following);
        }
        catch (Exception exception)
        {
            if (!following.IsCancellationRequested)
            {
                LogFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The board feed failed to show or load what it follows, and keeps following")]
    private partial void LogFailed(Exception exception);

    private static void ApplyWhileFollowing<T>(T state, Action<T> apply, CancellationToken following)
    {
        if (!following.IsCancellationRequested)
        {
            apply(state);
        }
    }
}
