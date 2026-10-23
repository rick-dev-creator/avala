using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Mentioning;

namespace Avala.Workbench.Drafting;

internal sealed record UndeliveredMessage(JobStatus Status, Option<JobRejection> Rejection);

internal sealed record WaitingMessage(MessageId Id, string Text, IReadOnlyList<Attachment> Attachments, bool Returned)
{
    public Option<UndeliveredMessage> Why { get; init; }

    public bool Equals(WaitingMessage? other) =>
        other is not null && Id == other.Id && string.Equals(Text, other.Text, StringComparison.Ordinal) && Returned == other.Returned && Why == other.Why
        && Attachments.SequenceEqual(other.Attachments);

    public override int GetHashCode() => HashCode.Combine(Id, Text, Returned);
}

internal sealed record Draft(string Text, IReadOnlyList<Attachment> Attachments, IReadOnlyList<Mention> Mentions)
{
    public static Draft Empty { get; } = new(string.Empty, [], []);

    public IReadOnlyList<WaitingMessage> Waiting { get; init; } = [];

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Attachments.Count == 0 && !Waiting.Any(message => message.Returned);

    public bool IsGone => IsEmpty && Waiting.Count == 0;

    public bool Equals(Draft? other) =>
        other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Attachments.SequenceEqual(other.Attachments) && Mentions.SequenceEqual(other.Mentions) && Waiting.SequenceEqual(other.Waiting);

    public override int GetHashCode() => HashCode.Combine(Text, Attachments.Count, Mentions.Count, Waiting.Count);
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
        ImmutableInterlocked.Update(ref drafts, current => kept.Where(pair => !current.ContainsKey(pair.Key) && !pair.Value.IsGone).Aggregate(current, (all, pair) => all.SetItem(pair.Key, pair.Value.Waiting.All(message => message.Returned) ? pair.Value : pair.Value with { Waiting = [.. pair.Value.Waiting.Select(message => message with { Returned = true })] })));
        Show();
    }

    public void Keep(JobId job, Draft draft) => Change(job, kept => draft with { Waiting = kept.Waiting });

    public void Wait(JobId job, IEnumerable<JobMessage> messages) =>
        Change(job, kept => kept with { Waiting = [.. kept.Waiting, .. messages.Select(message => new WaitingMessage(Id(message), message.Text, message.Attachments, false))] });

    public void Unwait(JobId job, IEnumerable<JobMessage> messages)
    {
        var ids = messages.Select(Id).ToHashSet();
        Change(job, kept => kept with { Waiting = [.. kept.Waiting.Where(message => message.Returned || !ids.Contains(message.Id))] });
    }

    public void Return(JobId job, IEnumerable<JobMessage> messages, UndeliveredMessage why)
    {
        var back = messages.Select(message => new WaitingMessage(Id(message), message.Text, message.Attachments, true) { Why = why }).ToList();
        var ids = back.Select(message => message.Id).ToHashSet();
        Change(job, kept => kept with { Waiting = [.. kept.Waiting.Where(message => !ids.Contains(message.Id)), .. back] });
    }

    public IReadOnlyList<WaitingMessage> TakeReturned(JobId job)
    {
        IReadOnlyList<WaitingMessage> taken = [];
        Change(job, kept =>
        {
            taken = [.. kept.Waiting.Where(message => message.Returned)];

            return kept with { Waiting = [.. kept.Waiting.Where(message => !message.Returned)] };
        });

        return taken;
    }

    private static MessageId Id(JobMessage message) => message.Messages.Count > 0 ? message.Messages[0] : MessageId.New();

    private void Change(JobId job, Func<Draft, Draft> change)
    {
        var changed = false;
        ImmutableInterlocked.Update(ref drafts, current =>
        {
            var draft = change(current.GetValueOrDefault(job, Draft.Empty));
            var next = draft.IsGone ? current.Remove(job) : current.SetItem(job, draft);
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

    private void Show() => board.Publish(Volatile.Read(ref drafts).Where(pair => !pair.Value.IsEmpty).Select(pair => pair.Key).ToImmutableHashSet());

    private void Schedule()
    {
        var timer = clock.CreateTimer(_ => Written = writes.RunAsync(WriteAsync, CancellationToken.None), null, Settle, Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref pending, timer)?.Dispose();
    }

    private Task WriteAsync(CancellationToken cancellationToken) => store.WriteAsync(Volatile.Read(ref drafts), cancellationToken);
}
