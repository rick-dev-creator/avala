using System.Collections.Concurrent;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Forges.Polling;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Forges.Tests.Polling;

internal sealed class PollerWorld : IAsyncDisposable
{
    public const string Repository = "/work/shop";

    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(60);

    public static readonly TriggerId Labeled = new(TriggerId.Machine, "labeled");

    public static readonly ForgeRule Rule = new("work", ForgeEventKind.IssueLabeled, ["alice"]) { Label = "avala" };

    public PollerWorld(params MemoryEventStore[] restored)
    {
        Store = restored.Length > 0 ? restored[0] : new MemoryEventStore();
        Book = new EventBook(Store, Bus, Clock);
        var connections = new ForgeConnections(new DeclaredEvents(), [Forge, new Watching.ScriptedForge()], new NoTransports());
        Poller = new ForgeEventPoller(Book, new EventSource(connections, new OriginRemote()), new EventDispatch([Triggers]), Clock);
        Log = new EventLogDesk(Book, Poller);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero));

    public RecordingBus Bus { get; } = new();

    public MemoryEventStore Store { get; }

    public EventForge Forge { get; } = new();

    public FakeTriggers Triggers { get; } = new();

    public EventBook Book { get; }

    public ForgeEventPoller Poller { get; }

    public IForgeEventLog Log { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static ForgeEvent LabeledBy(string actor, long id, string label = "avala") =>
        new(ForgeEventKind.IssueLabeled, id.ToString(System.Globalization.CultureInfo.InvariantCulture), id.ToString(System.Globalization.CultureInfo.InvariantCulture), actor) { Number = 12, Title = "Checkout button does nothing", Label = label };

    public async Task<PollerWorld> StartedAsync()
    {
        await Book.RunAsync(Cancellation);

        return this;
    }

    public async Task<ForgeEventWatch> CheckAsync() => Outcomes.Succeeds(await Log.CheckNowAsync(Labeled, Cancellation));

    public async Task SettledAsync() => _ = await Poller.CheckNowAsync(new TriggerId(TriggerId.Machine, "none"), Cancellation);

    public async ValueTask DisposeAsync() => await Poller.DisposeAsync();

    private sealed class DeclaredEvents : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(
                new ForgeSettings(
                    [
                        new ForgeDeclaration(new ForgeName("work"), EventForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None),
                        new ForgeDeclaration(new ForgeName("plain"), Watching.ScriptedForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None),
                    ],
                    Poll)));
    }

    private sealed class NoTransports : IForgeTransports
    {
        public IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target) => new Unused();

        public Option<ForgeError> Problem(ForgeDeclaration declaration) => Option<ForgeError>.None;
    }

    private sealed class Unused : IForgeApi
    {
        public ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class OriginRemote : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<string, ForgeError>.Success("https://forge.example/octo/shop.git"));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

internal sealed class MemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, EventCursor> cursors = new();
    private readonly ConcurrentQueue<ForgeEventEntry> entries = new();

    public IReadOnlyList<EventCursor> Cursors => [.. cursors.Values];

    public Task<IReadOnlyList<EventCursor>> CursorsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EventCursor>>([.. cursors.Values]);

    public Task KeepCursorAsync(EventCursor cursor, CancellationToken cancellationToken)
    {
        cursors[cursor.Trigger] = cursor;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ForgeEventEntry>> EntriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ForgeEventEntry>>([.. entries]);

    public Task AddEntryAsync(ForgeEventEntry entry, CancellationToken cancellationToken)
    {
        entries.Enqueue(entry);

        return Task.CompletedTask;
    }
}

internal sealed class EventForge : IForge, IForgeEvents
{
    public const string Id = "events";

    private readonly ConcurrentQueue<ForgeEventQuery> queries = new();

    public ForgeInfo Info { get; } = new(Id, "Event forge");

    public HookSignature Signature { get; } = new(["X-Event-Signature"], "sha256=");

    public List<ForgeEvent> Happened { get; } = [];

    public Option<ForgeError> Failure { get; set; }

    public IReadOnlyList<ForgeEventQuery> Queries => [.. queries];

    public List<string> Comments { get; } = [];

    public ValueTask<Result<ForgeEventPage, ForgeError>> ReadSinceAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        queries.Enqueue(query);

        if (Failure.IsSome)
        {
            return ValueTask.FromResult(Result<ForgeEventPage, ForgeError>.Failure(Failure.Match(error => error, () => ForgeError.Unreachable)));
        }

        var newest = Happened.Count == 0 ? "0" : Happened[^1].Cursor;
        var fresh = query.Cursor.Match(cursor => Happened.SkipWhile(happened => long.Parse(happened.Cursor, System.Globalization.CultureInfo.InvariantCulture) <= long.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture)).ToList(), () => []);

        return ValueTask.FromResult(Result<ForgeEventPage, ForgeError>.Success(new ForgeEventPage(fresh, newest)));
    }

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

    public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken)
    {
        Comments.Add($"#{pullRequest.Number}: {body}");

        return ValueTask.FromResult(Result<CommentRef, ForgeError>.Success(new CommentRef("1", Option<Uri>.None)));
    }
}

internal sealed class FakeTriggers : ITriggers
{
    private readonly ConcurrentQueue<FireRequest> fired = new();
    private readonly ConcurrentDictionary<string, bool> keys = new();

    public List<TriggerState> Declared { get; } =
        [new TriggerState(PollerWorld.Labeled, PollerWorld.Repository, TriggerKind.Forge, TriggerTarget.Job, Autonomy.Supervised, 1, true) { Forge = PollerWorld.Rule }];

    public IReadOnlyList<FireRequest> Fired => [.. fired];

    public WebhookEndpoint Endpoint => WebhookEndpoint.Off;

    public ValueTask<IReadOnlyList<TriggerState>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<TriggerState>>([.. Declared]);

    public ValueTask<Result<TriggerRun, TriggerError>> FireAsync(TriggerId trigger, FireRequest request, CancellationToken cancellationToken)
    {
        fired.Enqueue(request);
        var key = request.Key.Match(found => found, () => string.Empty);
        var duplicate = !keys.TryAdd(key, true);
        var run = new TriggerRun(Guid.CreateVersion7(), trigger, request.Origin, request.Who, DateTimeOffset.UnixEpoch, duplicate ? RunOutcome.Duplicate : RunOutcome.Submitted)
        {
            Job = duplicate ? Option<JobId>.None : JobId.New(),
            Key = request.Key,
        };

        return ValueTask.FromResult(Result<TriggerRun, TriggerError>.Success(run));
    }

    public IReadOnlyList<TriggerRun> RunsOf(TriggerId trigger) => [];

    public IReadOnlyList<WebhookDelivery> Deliveries() => [];

    public IReadOnlyList<TriggerFile> Files() => [];

    public ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
