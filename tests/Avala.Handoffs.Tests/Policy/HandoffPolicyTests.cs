using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.Policy;

public sealed class HandoffPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 1, 0, 0, TimeSpan.Zero);

    private static readonly ConnectionName Work = new("work");

    private static readonly ConnectionName Personal = new("personal");

    private static readonly ConnectionName Spare = new("spare");

    private static readonly ConnectionName Codex = new("codex");

    [Fact]
    public void AJobChosenByCapacityMayMoveAmongTheConnectionsItsChoiceCompared()
    {
        var situation = Situated(Chosen(Work, Personal, Codex), Rules(OnLimit.HandOffAnyHarness), Reading(Work, 0.95));

        Assert.Equal([Personal, Codex], HandoffPolicy.Candidates(situation).Select(candidate => candidate.Connection));
    }

    [Fact]
    public void TheRulesListNarrowsTheConnectionsAChosenJobMayMoveTo()
    {
        var situation = Situated(Chosen(Work, Personal, Codex), Rules(OnLimit.HandOffAnyHarness, Work, Codex), Reading(Work, 0.95));

        Assert.Equal([Codex], HandoffPolicy.Candidates(situation).Select(candidate => candidate.Connection));
    }

    [Fact]
    public void SameHarnessKeepsOnlyTheConnectionsOfTheJobsProvider()
    {
        var situation = Situated(Chosen(Work, Personal, Codex), Rules(OnLimit.HandOffSameHarness), Reading(Work, 0.95));

        Assert.Equal([Personal], HandoffPolicy.Candidates(situation).Select(candidate => candidate.Connection));
    }

    [Fact]
    public void AJobWhoseConnectionWasNamedIsNeverMovedUnlessTheRulesListIt()
    {
        var unlisted = Situated(Option<ConnectionChoice>.None, Rules(OnLimit.HandOffAnyHarness), Reading(Work, 0.95));
        var listed = Situated(Option<ConnectionChoice>.None, Rules(OnLimit.HandOffAnyHarness, Work, Spare), Reading(Work, 0.95));

        Assert.Empty(HandoffPolicy.Candidates(unlisted));
        Assert.Equal([Spare], HandoffPolicy.Candidates(listed).Select(candidate => candidate.Connection));
    }

    [Fact]
    public void UnderHoldNoConnectionIsACandidate() =>
        Assert.Empty(HandoffPolicy.Candidates(Situated(Chosen(Work, Personal), Rules(OnLimit.Hold), Reading(Work, 0.95))));

    [Fact]
    public void AConnectionUnderTheThresholdGoesOn() =>
        Assert.Equal(Verdict.GoOn, HandoffPolicy.ByThreshold(Situated(Chosen(Work, Personal), Rules(OnLimit.HandOffAnyHarness), Reading(Work, 0.85))).Verdict);

    [Fact]
    public void AJobAtTheThresholdMovesToTheLeastUsedCandidateAndTheChoiceSaysWhy()
    {
        var decision = HandoffPolicy.ByThreshold(Situated(
            Chosen(Work, Personal, Spare),
            Rules(OnLimit.HandOffSameHarness),
            Reading(Work, 0.95),
            Reading(Personal, 0.4),
            Reading(Spare, 0.2)));
        var choice = Outcomes.Present(decision.Choice);

        Assert.Equal((Verdict.HandOff, Spare, ChoiceReason.MostCapacity), (decision.Verdict, choice.Connection, choice.Reason));
        Assert.Equal([(Work, 0.95, false), (Personal, 0.4, true), (Spare, 0.2, true)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Used, candidate.Available)));
        Assert.Equal(new LimitReason(Work, "5h", 0.95, 0.9), Outcomes.Present(decision.Why));
    }

    [Fact]
    public void ATieGoesToTheCandidateListedFirst() =>
        Assert.Equal(
            Personal,
            Outcomes.Present(HandoffPolicy.ByThreshold(Situated(Chosen(Work, Personal, Spare), Rules(OnLimit.HandOffSameHarness), Reading(Work, 0.95))).Choice).Connection);

    [Fact]
    public void AWindowThatHasResetCountsAsUnused()
    {
        var reset = new UsageLimit("5h", 0.99, Now.AddMinutes(-1));
        var situation = Situated(Chosen(Work, Personal), Rules(OnLimit.HandOffSameHarness), Reading(Work, 0.95)) with
        {
            Usable = [new ConnectionState(Work, "claude-code", []), new ConnectionState(Personal, "claude-code", [reset])],
        };

        Assert.Equal(Personal, Outcomes.Present(HandoffPolicy.ByThreshold(situation).Choice).Connection);
    }

    [Fact]
    public void WithNoCandidateAvailableTheJobWaitsForTheEarliestEligibleConnectionBackUnderTheThreshold()
    {
        var decision = HandoffPolicy.ByThreshold(Situated(
            Chosen(Work, Personal),
            Rules(OnLimit.HandOffSameHarness),
            Reading(Work, 0.95, TimeSpan.FromHours(3)),
            Reading(Personal, 0.97, TimeSpan.FromHours(1))));

        Assert.Equal(Verdict.Wait, decision.Verdict);
        Assert.Equal(new ResumePoint(Personal, "5h", Now.AddHours(1)), Outcomes.Present(decision.Resume));
    }

    [Fact]
    public void AConnectionIsBackOnlyOnceEveryWindowOverTheThresholdHasReset()
    {
        var own = new ConnectionState(Work, "claude-code", [new UsageLimit("5h", 0.95, Now.AddHours(1)), new UsageLimit("7d", 0.92, Now.AddDays(2))]);
        var decision = HandoffPolicy.ByThreshold(new Situation(own, Chosen(Work), Rules(OnLimit.Hold), [own], Now));

        Assert.Equal(new ResumePoint(Work, "5h", Now.AddDays(2)), Outcomes.Present(decision.Resume));
    }

    [Fact]
    public void AWindowWithoutAResetLeavesTheJobWaitingForAPerson()
    {
        var own = new ConnectionState(Work, "claude-code", [new UsageLimit("5h", 0.95, Option<DateTimeOffset>.None)]);
        var decision = HandoffPolicy.ByThreshold(new Situation(own, Chosen(Work), Rules(OnLimit.Hold), [own], Now));

        Assert.Equal(new ResumePoint(Work, "5h", Option<DateTimeOffset>.None), Outcomes.Present(decision.Resume));
    }

    [Fact]
    public void AJobHeldBelowTheThresholdWaitsForItsMostUsedWindow()
    {
        var own = new ConnectionState(Work, "claude-code", [new UsageLimit("5h", 0.82, Now.AddHours(2)), new UsageLimit("7d", 0.3, Now.AddDays(3))]);
        var held = HandoffPolicy.WhenHeld(new Situation(own, Chosen(Work), Rules(OnLimit.Hold), [own], Now));

        Assert.Equal((Verdict.Wait, new ResumePoint(Work, "5h", Now.AddHours(2))), (held.Verdict, Outcomes.Present(held.Resume)));
        Assert.Equal(Verdict.GoOn, HandoffPolicy.ByThreshold(new Situation(own, Chosen(Work), Rules(OnLimit.Hold), [own], Now)).Verdict);
    }

    [Fact]
    public void AHeldJobWhoseReadingsHaveResetGoesOn()
    {
        var own = new ConnectionState(Work, "claude-code", [new UsageLimit("5h", 0.95, Now.AddMinutes(-1))]);

        Assert.Equal(Verdict.GoOn, HandoffPolicy.WhenHeld(new Situation(own, Chosen(Work), Rules(OnLimit.Hold), [own], Now)).Verdict);
    }

    private static Situation Situated(Option<ConnectionChoice> choice, LimitRules rules, params ConnectionState[] readings)
    {
        ConnectionState StateOf(ConnectionName name, string provider) =>
            readings.FirstOrDefault(reading => reading.Connection == name) is { } read ? read with { Provider = provider } : new ConnectionState(name, provider, []);

        IReadOnlyList<ConnectionState> usable = [StateOf(Work, "claude-code"), StateOf(Personal, "claude-code"), StateOf(Spare, "claude-code"), StateOf(Codex, "codex")];

        return new Situation(usable[0], choice, rules, usable, Now);
    }

    private static ConnectionState Reading(ConnectionName connection, double used) => Reading(connection, used, TimeSpan.FromHours(2));

    private static ConnectionState Reading(ConnectionName connection, double used, TimeSpan resetsIn) =>
        new(connection, string.Empty, [new UsageLimit("5h", used, Now + resetsIn)]);

    private static Option<ConnectionChoice> Chosen(params ConnectionName[] compared) =>
        new ConnectionChoice(compared[0], ChoiceReason.MostCapacity, [.. compared.Select(name => new CandidateCapacity(name, 0, Option<UsageLimit>.None, 1, true))], Now);

    private static LimitRules Rules(OnLimit action, params ConnectionName[] listed) => new(action, 0.9, listed);
}
