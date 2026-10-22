using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class InvariantTests
{
    private static SessionId Session => new(Guid.Parse("0199f0c4-6a00-7000-8000-000000000001"));

    private static readonly DateTimeOffset Moment = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AScenarioWhoseJobsKeptTheirLifecycleBreaksNoInvariant() =>
        Assert.Empty(Invariants.BrokenIn(Of(
            Job(JobStatus.Running, AttemptOutcome.Running),
            Job(JobStatus.Approved, AttemptOutcome.Passed) with { Summary = Summary(JobStatus.Approved) with { Ended = Moment } },
            Job(JobStatus.NeedsHelp, AttemptOutcome.Rejected),
            Job(JobStatus.NeedsHelp, AttemptOutcome.Interrupted) with { Summary = Summary(JobStatus.NeedsHelp) with { Hold = HoldReason.Stalled } })));

    [Fact]
    public void AJobThatFailedWithoutItsReasonBreaksAnInvariant() =>
        Broken("A job fails exactly when it records why", Of(Job(JobStatus.Failed, AttemptOutcome.Interrupted) with { Summary = Summary(JobStatus.Failed) with { Ended = Moment } }));

    [Fact]
    public void AHoldOnAJobThatNoLongerNeedsHelpBreaksAnInvariant() =>
        Broken("A job's hold agrees with its status", Of(Job(JobStatus.Running, AttemptOutcome.Running) with { Summary = Summary(JobStatus.Running) with { Hold = HoldReason.Stalled } }));

    [Fact]
    public void AJobThatNeedsHelpWithNeitherAHoldNorARejectedAttemptBreaksAnInvariant() =>
        Broken("A job's hold agrees with its status", Of(Job(JobStatus.NeedsHelp, AttemptOutcome.Interrupted)));

    [Fact]
    public void AnApprovedJobWithoutItsEndBreaksAnInvariant() =>
        Broken("A job has ended exactly when it is approved, discarded or failed", Of(Job(JobStatus.Approved, AttemptOutcome.Passed)));

    [Fact]
    public void AnAttemptLeftUnderwayOnceItsJobSettledBreaksAnInvariant() =>
        Broken("Only the last attempt of a working job is underway", Of(Job(JobStatus.AwaitingReview, AttemptOutcome.AwaitingCheck)));

    [Fact]
    public void AnEndedJobWhoseSessionIsStillOpenBreaksAnInvariant() =>
        Broken("An ended job stops its session", Of(Discarded()) with { OpenSessions = [Session] });

    [Fact]
    public void AnEndedJobWithAProcessTreeStillOpenInItsWorktreeBreaksAnInvariant()
    {
        var job = Discarded();
        var worktree = Path.Combine(Path.GetTempPath(), "worktrees", "greet");

        Broken("An ended job leaves no process behind", Of(job) with { OpenTrees = [Path.Combine(worktree, "site")], Worktrees = new Dictionary<JobId, string> { [job.Summary.Job] = worktree } });
    }

    [Fact]
    public void AChildStillRunningAfterItsParentEndedBreaksAnInvariant()
    {
        var parent = Discarded();

        Broken("No child outlives its parent's end", Of(parent, Job(JobStatus.Running, AttemptOutcome.Running) with { Summary = Summary(JobStatus.Running) with { Parent = parent.Summary.Job } }));
    }

    [Fact]
    public void ATurnStillOpenAfterItsSessionEndedBreaksAnInvariant() =>
        Broken("No turn stays open once its session ended", Of() with { TurnsOpenAfterTheirSession = [new OpenTurn(Session, TurnId.New())] });

    [Fact]
    public async Task AnItemStillOpenAfterItsTurnEndedBreaksAnInvariantAsync()
    {
        var ledger = new TurnLedger();
        var turn = TurnId.New();

        await PlayAsync(ledger, new TurnStarted(Session, turn), new ItemStarted(Session, turn, new ItemId("survey"), ItemKind.Subagent, "Survey"), new TurnCompleted(Session, turn, TurnOutcome.Finished));

        Broken("No item stays open once its turn ended", Of() with { ItemsOpenAfterTheirTurn = ledger.OpenAfterTheirTurn });
    }

    [Fact]
    public async Task ASessionRestedWhileItsBackgroundWorkRunsBreaksAnInvariantAsync()
    {
        var ledger = new TurnLedger();
        var turn = TurnId.New();
        await PlayAsync(ledger, new TurnStarted(Session, turn), new ItemStarted(Session, turn, new ItemId("survey"), ItemKind.Subagent, "Survey"), new ItemBackgrounded(Session, turn, new ItemId("survey")), new TurnCompleted(Session, turn, TurnOutcome.Finished));

        await ledger.HandleAsync(new JobSessionRested(JobId.New(), Session), TestContext.Current.CancellationToken);

        Broken("A session with work pending is never rested", Of() with { RestsWhileWorking = ledger.RestedWhileWorking, ItemsOpenAfterTheirTurn = [] });
    }

    [Fact]
    public void NegativeUsageBreaksAnInvariant() =>
        Broken("Usage is never negative", Of() with { Usage = [new UsageSummary(new TokenUsage(-1, 0, 0, 0, 0), [], 0, default, [])] });

    private static async Task PlayAsync(TurnLedger ledger, params IAgentEvent[] events)
    {
        foreach (var happened in events)
        {
            await ledger.HandleAsync(new AgentActivity(happened), TestContext.Current.CancellationToken);
        }
    }

    private static void Broken(string invariant, Scene scene) =>
        Assert.StartsWith(invariant, Assert.Single(Invariants.BrokenIn(scene)), StringComparison.Ordinal);

    private static Scene Of(params JobHistory[] jobs) => new(jobs, [], [], new Dictionary<JobId, string>(), [], [], [], []);

    private static JobHistory Discarded() =>
        Job(JobStatus.Discarded, AttemptOutcome.Interrupted) with { Summary = Summary(JobStatus.Discarded) with { Ended = Moment } };

    private static JobHistory Job(JobStatus status, AttemptOutcome last) =>
        new(Summary(status), [new SessionRecord(Session, [1])], [new AttemptRecord(1, AttemptOrigin.Initial, last, Option<string>.None, Session)]);

    private static JobSummary Summary(JobStatus status) =>
        new(JobId.New(), "/repos/shop", "Greet the team", Moment, status, Option<Avala.Agents.Contracts.Connections.ConnectionName>.None, Option<Autonomy>.None, Option<WorkspaceId>.None);
}
