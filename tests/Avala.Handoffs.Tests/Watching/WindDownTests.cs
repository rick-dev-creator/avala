using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Tests.Watching;

public sealed class WindDownTests
{
    private static readonly LimitRules Rules = new(OnLimit.HandOffSameHarness, 0.9, [Watched.Work, Watched.Personal]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AtTheThresholdARunningJobIsAskedOnceToLeaveItsWorkInAValidStateAsync()
    {
        var winding = Winding(Rules);

        await winding.RecordedAsync(0.92);
        await winding.RecordedAsync(0.93);

        Assert.Equal(["steer"], winding.Operations);
        Assert.Contains("92% spent", winding.Watched.Jobs.Calls.Entries[0].Text, StringComparison.Ordinal);
        Assert.Equal([RelayMarkKind.WindingDown], winding.Watched.Book.MarksOf(winding.Watched.Job).Select(mark => mark.Kind));
    }

    [Fact]
    public async Task AJobWindingDownIsStoppedWhenItsUsageReachesTheHardCapAsync()
    {
        var winding = Winding(Rules);

        await winding.RecordedAsync(0.92);
        await winding.RecordedAsync(0.99);
        await winding.RecordedAsync(1);

        Assert.Equal(["steer", "hold"], winding.Operations);
        Assert.Equal(
            [RelayMarkKind.WindingDown, RelayMarkKind.Capped],
            winding.Watched.Book.MarksOf(winding.Watched.Job).Select(mark => mark.Kind));
    }

    [Fact]
    public async Task AReadingThatJumpsPastTheHardCapStopsTheJobWithoutWindingDownAsync()
    {
        var winding = Winding(Rules);

        await winding.RecordedAsync(0.99);

        Assert.Equal(["hold"], winding.Operations);
    }

    [Fact]
    public async Task WithNoOtherConnectionWithRoomTheJobKeepsWorkingAsync()
    {
        var winding = Winding(Rules);
        winding.Watched.Reading(Watched.Personal, 0.95, Option<DateTimeOffset>.None);

        await winding.RecordedAsync(0.92);

        Assert.Empty(winding.Operations);
    }

    [Fact]
    public async Task BelowTheThresholdNothingHappensAsync()
    {
        var winding = Winding(Rules);

        await winding.RecordedAsync(0.85);

        Assert.Empty(winding.Operations);
    }

    [Fact]
    public async Task AJobThatCannotBeSteeredIsStoppedInsteadAsync()
    {
        var winding = Winding(Rules);
        winding.Watched.Jobs.SteerRefusal = JobRejection.NotRunning;

        await winding.RecordedAsync(0.92);

        Assert.Equal(["steer", "hold"], winding.Operations);
    }

    [Fact]
    public async Task AJobThatMovedToAnotherSessionBeforeItsWindDownArrivedIsLeftAloneAsync()
    {
        var winding = Winding(Rules);
        winding.Watched.Jobs.RunningOn = SessionId.New();

        await winding.RecordedAsync(0.92);

        Assert.Empty(winding.Operations);
        Assert.Empty(winding.Watched.Book.MarksOf(winding.Watched.Job));
    }

    [Fact]
    public async Task AJobThatIsNotRunningIsLeftAloneAsync()
    {
        var winding = Winding(Rules);
        winding.Watched.Status(JobStatus.NeedsHelp);

        await winding.RecordedAsync(0.92);

        Assert.Empty(winding.Operations);
    }

    [Fact]
    public async Task UsageOfASessionTheJobHasLeftIsIgnoredAsync()
    {
        var winding = Winding(Rules);

        await winding.RecordedAsync(SessionId.New(), 0.92);

        Assert.Empty(winding.Operations);
    }

    [Fact]
    public async Task TheGateRelaysTheTurnOfAWoundDownSessionOnceAsync()
    {
        var winding = Winding(Rules);
        var gate = new RelayGate(winding.Ledger);
        await winding.RecordedAsync(0.92);

        var first = await gate.EvaluateAsync(winding.Attempt(winding.Watched.Session), Cancellation);
        var second = await gate.EvaluateAsync(winding.Attempt(winding.Watched.Session), Cancellation);

        Assert.Equal((GateDecision.Retry, RelayGate.Feedback), (first.Decision, first.Feedback));
        Assert.Equal(GateDecision.Pass, second.Decision);
    }

    [Fact]
    public async Task TheGatePassesTheTurnOfASessionThatIsNotWindingDownAsync()
    {
        var gate = new RelayGate(new RelayLedger());

        var verdict = await gate.EvaluateAsync(Winding(Rules).Attempt(SessionId.New()), Cancellation);

        Assert.Equal(GateDecision.Pass, verdict.Decision);
    }

    private static WindingJob Winding(LimitRules rules)
    {
        var watched = new Watched();
        watched.Rules.Rules = rules;
        watched.Status(JobStatus.Running);
        watched.Reading(Watched.Personal, 0.2, Option<DateTimeOffset>.None);

        return new WindingJob(watched);
    }

    private sealed class WindingJob
    {
        public WindingJob(Watched watched)
        {
            Watched = watched;
            Down = new WindDown(watched.Situations, watched.Usage, new RelayMoves(watched.Jobs, watched.Book, Ledger), Ledger);
        }

        public Watched Watched { get; }

        public RelayLedger Ledger { get; } = new();

        public WindDown Down { get; }

        public IReadOnlyList<string> Operations => [.. Watched.Jobs.Calls.Entries.Select(call => call.Operation)];

        public Task RecordedAsync(double used) => RecordedAsync(Watched.Session, used);

        public async Task RecordedAsync(SessionId session, double used)
        {
            Watched.Reading(Watched.Work, used, Watched.Start.AddHours(2));
            Watched.Usage.Spent[session] = new UsageSummary(default, [], 0, default, [new UsageLimit("5h", used, Watched.Start.AddHours(2))]);
            Watched.Clock.Advance(TimeSpan.FromMinutes(2));
            await Down.HandleAsync(new UsageRecorded(session, Watched.Job), Cancellation);
        }

        public CompletedAttempt Attempt(SessionId session) =>
            new(Watched.Job, 1, Worktrees.Path, Watched.Instruction) { Turn = new FinishedTurn(session, TurnId.New()) };
    }
}
