using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Escalating;
using Avala.Delegation.Policy;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Delegation.Resuming;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Delegation.Tests.Delegating;

internal sealed class Desk : IAsyncDisposable
{
    public const string Worktree = "/worktrees/parent";

    public static readonly DelegationRules Declared = new([new ConnectionName("work"), new ConnectionName("personal")], Routing.RoundRobin, 1, 2);

    private readonly DelegationDesk desk;
    private readonly ChildReporter reporter;

    public Desk(Result<Option<DelegationRules>, DelegationError> rules)
    {
        Book = new DelegationBook(Store);
        Terms = new ChildTerms(Book, Jobs);
        var journal = new DelegationJournal(Book, Bus, Agents, Clock);
        var policy = new DelegationPolicy(new FixedRules(rules), Jobs, Audit, new ConnectionRouter([Selector]));
        reporter = new ChildReporter(
            Jobs,
            new ChildEvidence(Changes, Verifications, Jobs, new ChildSpending(Usage, [Budgets])),
            journal,
            NullLogger<ChildReporter>.Instance);
        desk = new DelegationDesk(new Delegator(policy, Jobs, journal, Terms), reporter, Jobs, Book);
        Parents = new DeferredParents(Book);
        Resumption = new ParentResumption(Parents, Jobs);
        Briefing = new OwedReports(Book, journal);
        Notes = new ParentNotes(journal, Jobs, Answers, Clock);
        Waits = new ChildWaits(reporter, journal, Audit, Answers);
        Parent = Jobs.Running();
    }

    public Desk()
        : this(Option<DelegationRules>.Some(Declared))
    {
    }

    public RecordingParentAnswers Answers { get; } = new();

    public ParentNotes Notes { get; }

    public ChildWaits Waits { get; }

    public DeferredParents Parents { get; }

    public ParentResumption Resumption { get; }

    public OwedReports Briefing { get; }

    public async Task StartupCompletedAsync()
    {
        await desk.HandleAsync(new StartupCompleted(), Cancellation);
        await Resumption.HandleAsync(new StartupCompleted(), Cancellation);
    }

    public async Task<DelegationRecord> ReportedToResumptionAsync(JobId child)
    {
        var reported = await ReportedAsync(child);
        await Resumption.HandleAsync(new ChildReported(reported), Cancellation);

        return reported;
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    public Records.InMemoryDelegations Store { get; } = new();

    public RecordingBus Bus { get; } = new();

    public ReturningAgents Agents { get; } = new();

    public FakeJobs Jobs { get; } = new();

    public FixedAudit Audit { get; } = new();

    public FixedUsage Usage { get; } = new();

    public FixedSelector Selector { get; } = new();

    public FixedBudgets Budgets { get; } = new();

    public FixedChanges Changes { get; } = new();

    public FixedVerifications Verifications { get; } = new();

    public DelegationBook Book { get; }

    public ChildTerms Terms { get; }

    public JobId Parent { get; }

    public SessionId Session { get; } = SessionId.New();

    public TurnId Turn { get; } = TurnId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task StartedAsync(Autonomy autonomy = Autonomy.Supervised) => await StartedAsync(Parent, autonomy);

    public async Task StartedAsync(JobId job, Autonomy autonomy = Autonomy.Supervised)
    {
        Audit.Effective[Session] = autonomy;
        await desk.HandleAsync(new SessionOpened(Session, new ProviderInfo("simulator", "Simulator"), Worktree, new ConnectionName("work")), Cancellation);
        await desk.HandleAsync(new JobSessionStarted(job, Session), Cancellation);
    }

    public async Task CallAsync(string item, string input) =>
        await desk.HandleAsync(new AgentActivity(new ToolCalled(Session, Turn, new ItemId(item), DelegationTool.Name, input)), Cancellation);

    public async Task<JobId> DelegateAsync(string item, string instruction = "Write the release notes")
    {
        await CallAsync(item, $$"""{ "instruction": "{{instruction}}" }""");

        return Assert.Single(Book.All(), record => record.Item == new ItemId(item)).Child.Match(child => child, () => throw new InvalidOperationException("Not delegated"));
    }

    public async Task ReplyAsync(JobId child, params string[] chunks)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var item = new ItemId($"reply-{chunks.Length}");
        await desk.HandleAsync(new JobSessionStarted(child, session), Cancellation);
        await desk.HandleAsync(new AgentActivity(new ItemStarted(session, turn, item, ItemKind.Message, "Reply")), Cancellation);

        foreach (var chunk in chunks)
        {
            await desk.HandleAsync(new AgentActivity(new ItemProgressed(session, turn, item, chunk)), Cancellation);
        }
    }

    public async Task ProgressAsync(JobId job, JobStatus status) => await desk.HandleAsync(new JobProgressed(job, status), Cancellation);

    public async Task HeldAsync(JobId job, HoldReason reason) =>
        await desk.HandleAsync(new JobHeld(new JobHold(job, SessionId.New(), reason, SessionHalt.Interrupted)), Cancellation);

    public async Task<DelegationRecord> ReportedAsync(JobId child) =>
        (await Bus.WaitForAsync<ChildReported>(reported => reported.Delegation.Child == Option<JobId>.Some(child), Cancellation)).Delegation;

    public async Task WaitChildAsync(string item, JobId child) =>
        await Waits.HandleAsync(new AgentActivity(new ToolCalled(Session, Turn, new ItemId(item), WaitChildTool.Name, $$"""{ "child": "{{child.Value}}" }""")), Cancellation);

    public async ValueTask DisposeAsync()
    {
        await Notes.DisposeAsync();
        await reporter.DisposeAsync();
    }
}
