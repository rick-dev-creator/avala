using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Briefing;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Records;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Handoffs.Tests.Watching;

internal sealed class Watched : IAsyncDisposable
{
    public const string Instruction = "Fix the calculator";

    public static readonly ConnectionName Work = new("work");

    public static readonly ConnectionName Personal = new("personal");

    public static readonly ProviderInfo Agent = new("agent", "Agent");

    public static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    public Watched()
    {
        Connections.Declared.AddRange([new DeclaredConnection(Work, Agent.Id, Option<string>.None), new DeclaredConnection(Personal, Agent.Id, Option<string>.None)]);
        Book = new HandoffBook(Store, new JobSpending(Usage, Usage), Connections, Bus);
        Briefs = new BriefGatherer(Worktrees, Audit, Audit, new JobNotes(Audit));
        Situations = new Situations(new JobPlaces(Catalog, Worktrees, Rules, []), new ConnectionReadings(Connections, Usage), Clock);
        Compaction = new Compaction(Agents, Connections, Clock);
        Agents.Answer = (session, turn) => Compaction.HandleAsync(new TurnFinished(session, turn, TurnOutcome.Finished), Cancellation).AsTask();
        Watch = new LimitWatch(
            Situations,
            new LimitActions(Jobs, Briefs, Book, Compaction),
            Store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LimitWatch>.Instance);
        Status(JobStatus.NeedsHelp);
    }

    public NoticingClock Clock { get; } = new(Start);

    public RecordingBus Bus { get; } = new();

    public KeptHandoffs Store { get; } = new();

    public MovingJobs Jobs { get; } = new();

    public KnownJobs Catalog { get; } = new();

    public Worktrees Worktrees { get; } = new();

    public FixedRules Rules { get; } = new();

    public DeclaredConnections Connections { get; } = new();

    public MeasuredUsage Usage { get; } = new();

    public Audit Audit { get; } = new();

    public HandoffBook Book { get; }

    public CompactingAgents Agents { get; } = new();

    public Compaction Compaction { get; }

    public SessionId Session { get; } = SessionId.New();

    public BriefGatherer Briefs { get; }

    public LimitWatch Watch { get; }

    public Situations Situations { get; }

    public JobId Job { get; } = JobId.New();

    public ConnectionName Running { get; set; } = Work;

    public bool Companion { get; init; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Reading(ConnectionName connection, double used, Option<DateTimeOffset> resets) =>
        Usage.Connections[connection] = new ConnectionUsage(connection, Agent, new UsageSummary(default, [], 0, default, [new UsageLimit("5h", used, resets)]));

    public void Status(JobStatus status) =>
        Catalog.Histories[Job] = new JobHistory(
            new JobSummary(Job, "/repos/shop", Instruction, Start, status, Running, Option<Autonomy>.None, WorkspaceId.New()) { Companion = Companion },
            [new SessionRecord(Session, [1])],
            []);

    public async Task HeldAsync(HoldReason reason) => await HeldAsync(reason, Option<UsageLimit>.None);

    public async Task HeldAsync(HoldReason reason, Option<UsageLimit> limit) =>
        await Watch.HandleAsync(new JobHeld(new JobHold(Job, SessionId.New(), reason, SessionHalt.Interrupted) { Limit = limit }), Cancellation);

    public async Task ProgressedAsync(JobStatus status) => await Watch.HandleAsync(new JobProgressed(Job, status), Cancellation);

    public async Task<ResetWait> WaitingAsync() => (await Bus.WaitForAsync<JobWaitsForReset>(found => found.Wait.Job == Job, Cancellation)).Wait;

    public async Task DroppedAsync() => _ = await Store.Calls.WaitForAsync(call => call == $"drop {Job.Value}");

    public async Task<JobCall> MovedAsync() => await Jobs.Calls.WaitForAsync(call => call.Job == Job);

    public ValueTask DisposeAsync() => Watch.DisposeAsync();
}
