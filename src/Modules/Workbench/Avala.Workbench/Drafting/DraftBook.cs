using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Mentioning;

namespace Avala.Workbench.Drafting;

internal sealed record Draft(string Text, IReadOnlyList<Attachment> Attachments, IReadOnlyList<Mention> Mentions)
{
    public static Draft Empty { get; } = new(string.Empty, [], []);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Attachments.Count == 0;
}

internal interface IDraftStore
{
    Task<IReadOnlyDictionary<JobId, Draft>> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(IReadOnlyDictionary<JobId, Draft> drafts, CancellationToken cancellationToken);
}

internal sealed class DraftBook(IDraftStore store, JobBoard board, TimeProvider clock) : IStartupTask, IAsyncDisposable
{
    public static TimeSpan Settle { get; } = TimeSpan.FromMilliseconds(400);

    private readonly SerialExecutor writes = new();
    private ImmutableDictionary<JobId, Draft> drafts = ImmutableDictionary<JobId, Draft>.Empty;
    private ITimer? pending;
    private int disposed;

    public StartupStage Stage => StartupStage.Restore;

    public Task Written { get; private set; } = Task.CompletedTask;

    public Option<Draft> Of(JobId job) => Volatile.Read(ref drafts).TryGetValue(job, out var found) ? found : Option<Draft>.None;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var kept = await store.ReadAsync(cancellationToken);
        ImmutableInterlocked.Update(ref drafts, current => kept.Where(pair => !current.ContainsKey(pair.Key) && !pair.Value.IsEmpty).Aggregate(current, (all, pair) => all.SetItem(pair.Key, pair.Value)));
        Show();
    }

    public void Keep(JobId job, Draft draft)
    {
        var changed = false;
        ImmutableInterlocked.Update(ref drafts, current =>
        {
            var next = draft.IsEmpty ? current.Remove(job) : current.SetItem(job, draft);
            changed = !ReferenceEquals(next, current);

            return next;
        });

        if (changed)
        {
            Show();
            Schedule();
        }
    }

    public void Forget(JobId job) => Keep(job, Draft.Empty);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref pending, null)?.Dispose();
        await writes.RunAsync(WriteAsync, CancellationToken.None);
        await writes.DisposeAsync();
    }

    private void Show() => board.Publish(Volatile.Read(ref drafts).Keys.ToImmutableHashSet());

    private void Schedule()
    {
        var timer = clock.CreateTimer(_ => Written = writes.RunAsync(WriteAsync, CancellationToken.None), null, Settle, Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref pending, timer)?.Dispose();
    }

    private Task WriteAsync(CancellationToken cancellationToken) => store.WriteAsync(Volatile.Read(ref drafts), cancellationToken);
}
