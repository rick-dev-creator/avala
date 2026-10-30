using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Handoffs.Tests.Watching;

public sealed class ElasticRelayAdversaryTests
{
    private static readonly LimitRules Relaying = new(OnLimit.HandOffSameHarness, 0.9, [Watched.Work, Watched.Personal]);

    private static readonly ConnectionName Spare = new("spare");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionThatGoesOnAfterItsWindowResetIsWoundDownAgainWhenItsNextWindowFillsAsync()
    {
        var relay = Relay(Relaying);
        await relay.RecordedAsync(0.92, Watched.Start.AddHours(2), TimeSpan.FromMinutes(2));
        var wound = await new RelayGate(relay.Ledger).EvaluateAsync(relay.Attempt(), Cancellation);
        await relay.RecordedAsync(0.93, Watched.Start.AddHours(7), TimeSpan.FromHours(3));

        Assert.Equal(GateDecision.Retry, wound.Decision);
        Assert.Equal(["steer", "steer"], relay.Operations);
    }

    [Fact]
    public async Task ASessionResumedAfterItsWindowResetIsStoppedAgainAtTheHardCapOfItsNextWindowAsync()
    {
        var relay = Relay(Relaying);
        await relay.RecordedAsync(0.99, Watched.Start.AddHours(2), TimeSpan.FromMinutes(2));
        await relay.RecordedAsync(0.99, Watched.Start.AddHours(7), TimeSpan.FromHours(3));

        Assert.Equal(["hold", "hold"], relay.Operations);
    }

    [Fact]
    public async Task WithoutWindingDownAJobPastItsThresholdFinishesItsTurnAndIsRelayedAtItsEndAsync()
    {
        var relay = Relay(Relaying with { WindDown = false });
        await relay.RecordedAsync(0.92, Watched.Start.AddHours(2), TimeSpan.FromMinutes(2));
        var verdict = await new RelayGate(relay.Ledger).EvaluateAsync(relay.Attempt(), Cancellation);

        Assert.Empty(relay.Operations);
        Assert.Equal(GateDecision.Retry, verdict.Decision);
    }

    [Fact]
    public async Task ALowerThresholdSavedWhileAJobRunsAppliesToThatJobAtOnceAsync()
    {
        var relay = Relay(Relaying);
        await relay.RecordedAsync(0.5, Watched.Start.AddHours(2), TimeSpan.FromMinutes(2));
        relay.Watched.Rules.Rules = Relaying with { Threshold = 0.6 };
        await relay.RecordedAsync(0.7, Watched.Start.AddHours(2), TimeSpan.FromMinutes(2));

        Assert.Equal(["steer"], relay.Operations);
    }

    [Fact]
    public async Task AJobAtTheHardCapIsStoppedEvenWhenAConnectionFreedUpWithinAMinuteOfTheLastLookAsync()
    {
        var relay = Relay(Relaying);
        relay.Watched.Reading(Watched.Personal, 0.95, Watched.Start.AddMinutes(1));
        await relay.RecordedAsync(0.92, Watched.Start.AddHours(2), TimeSpan.FromSeconds(10));
        relay.Watched.Reading(Watched.Personal, 0.05, Watched.Start.AddHours(5));
        await relay.RecordedAsync(0.99, Watched.Start.AddHours(2), TimeSpan.FromSeconds(20));

        Assert.Equal(["hold"], relay.Operations);
    }

    [Fact]
    public async Task AJobHeldWithNoUsageReportedDoesNotGoOnAtOnceWhenAvalaRestartsBeforeItsGuessedResetAsync()
    {
        await using var watched = new Watched();
        watched.Rules.Rules = LimitRules.Default with { Resume = ResumeAtReset.Auto };
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        var wait = await watched.WaitingAsync();
        await watched.Watch.DisposeAsync();
        await using var restarted = new LimitWatch(watched.Situations, new LimitActions(watched.Jobs, watched.Briefs, watched.Book, watched.Compaction), watched.Store, NullLogger<LimitWatch>.Instance);
        await restarted.RunAsync(Cancellation);

        Assert.Equal(Option<DateTimeOffset>.Some(Watched.Start.AddHours(5)), wait.ResumesAt);
        Assert.Empty(watched.Jobs.Calls.Entries);
    }

    [Fact]
    public async Task AnAutoJobAPersonMovedStillRelaysAmongTheConnectionsAutoComparedAsync()
    {
        await using var watched = Auto();
        watched.Reading(Watched.Work, 0.4, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Watched.Start.AddHours(3));
        watched.Reading(Spare, 0.1, Watched.Start.AddHours(3));
        Outcomes.Succeeds(await new PersonHandoff(watched.Situations, watched.Briefs, watched.Jobs).HandOffAsync(watched.Job, Watched.Personal, Cancellation));
        var moved = Outcomes.Present(Assert.Single(watched.Jobs.Calls.Entries).Handoff);
        Moved(watched, moved.Choice);
        watched.Reading(Watched.Personal, 0.95, Watched.Start.AddHours(3));
        var decision = HandoffPolicy.ByThreshold(Outcomes.Present(await watched.Situations.OfAsync(watched.Job, Cancellation)).Situation);

        Assert.Equal(Verdict.HandOff, decision.Verdict);
        Assert.Equal(Spare, Outcomes.Present(decision.Choice).Connection);
    }

    [Fact]
    public async Task AConnectionThatWasBrieflyUnusableAtOneHandoffStaysInTheRelayOfAnAutoJobAsync()
    {
        await using var watched = Auto();
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Watched.Start.AddHours(3));
        watched.Connections.Unusable.Add(Spare);
        var first = HandoffPolicy.ByThreshold(Outcomes.Present(await watched.Situations.OfAsync(watched.Job, Cancellation)).Situation);
        watched.Connections.Unusable.Remove(Spare);
        Moved(watched, Outcomes.Present(first.Choice));
        watched.Reading(Watched.Personal, 0.95, Watched.Start.AddHours(3));
        watched.Reading(Spare, 0.1, Watched.Start.AddHours(3));
        var second = HandoffPolicy.ByThreshold(Outcomes.Present(await watched.Situations.OfAsync(watched.Job, Cancellation)).Situation);

        Assert.Equal(Watched.Personal, Outcomes.Present(first.Choice).Connection);
        Assert.Equal(Verdict.HandOff, second.Verdict);
    }

    private static Watched Auto()
    {
        var watched = new Watched();
        watched.Connections.Declared.Add(new DeclaredConnection(Spare, Watched.Agent.Id, Option<string>.None));
        watched.Rules.Rules = new LimitRules(OnLimit.HandOffSameHarness, 0.9, []);
        Moved(watched, new ConnectionChoice(Watched.Work, ChoiceReason.MostCapacity, [.. new[] { Watched.Work, Watched.Personal, Spare }.Select(name => new CandidateCapacity(name, 0, Option<UsageLimit>.None, 1, true))], Watched.Start));

        return watched;
    }

    private static void Moved(Watched watched, ConnectionChoice choice)
    {
        watched.Running = choice.Connection;
        watched.Status(JobStatus.Running);
        watched.Catalog.Histories[watched.Job] = watched.Catalog.Histories[watched.Job] with { Choice = choice };
    }

    private static Relayed Relay(LimitRules rules)
    {
        var watched = new Watched();
        watched.Rules.Rules = rules;
        watched.Status(JobStatus.Running);
        watched.Reading(Watched.Personal, 0.2, Option<DateTimeOffset>.None);

        return new Relayed(watched);
    }

    private sealed class Relayed
    {
        public Relayed(Watched watched)
        {
            Watched = watched;
            Down = new WindDown(watched.Situations, watched.Usage, new RelayMoves(watched.Jobs, watched.Book, Ledger), Ledger);
        }

        public Watched Watched { get; }

        public RelayLedger Ledger { get; } = new();

        public WindDown Down { get; }

        public IReadOnlyList<string> Operations => [.. Watched.Jobs.Calls.Entries.Select(call => call.Operation)];

        public async Task RecordedAsync(double used, DateTimeOffset resets, TimeSpan after)
        {
            Watched.Clock.Advance(after);
            Watched.Reading(Watched.Work, used, resets);
            Watched.Usage.Spent[Watched.Session] = new UsageSummary(default, [], 0, default, [new UsageLimit("5h", used, resets)]);
            await Down.HandleAsync(new UsageRecorded(Watched.Session, Watched.Job), Cancellation);
        }

        public CompletedAttempt Attempt() =>
            new(Watched.Job, 1, Worktrees.Path, Watched.Instruction) { Turn = new FinishedTurn(Watched.Session, TurnId.New()) };
    }
}
