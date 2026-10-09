using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using static Avala.Observability.Tests.Tracking.Tracked;

namespace Avala.Observability.Tests.Tracking;

public sealed class UsageTrackingTests
{
    [Fact]
    public async Task UsageAddsUpBySessionJobAndProviderWithCostsPerCurrencyAsync()
    {
        using var tracked = new Tracked();
        var job = JobId.New();
        var first = await tracked.OpenAsync(Claude, job);
        var resumed = await tracked.OpenAsync(Claude, job);
        var other = await tracked.OpenAsync(Codex);

        await tracked.SeeAsync(
            Usage(first, new TokenUsage(100, 10, 50, 5, 2), new Cost(0.01m, "USD")),
            Usage(resumed, new TokenUsage(200, 20, 0, 0, 4), new Cost(0.02m, "USD")),
            Usage(resumed, new TokenUsage(1, 1, 1, 1, 1), new Cost(0.50m, "EUR")),
            Usage(other, new TokenUsage(300, 30, 0, 0, 0), new Cost(0.03m, "USD")));

        Assert.Equal(new TokenUsage(100, 10, 50, 5, 2), Outcomes.Present(tracked.Book.OfSession(first)).Tokens);
        var ofJob = Outcomes.Present(tracked.Book.OfJob(job));
        Assert.Equal(new TokenUsage(301, 31, 51, 6, 7), ofJob.Tokens);
        Assert.Equal([new Cost(0.50m, "EUR"), new Cost(0.03m, "USD")], ofJob.Costs);
        Assert.Equal(
            [("claude", new TokenUsage(301, 31, 51, 6, 7)), ("codex", new TokenUsage(300, 30, 0, 0, 0))],
            tracked.Book.ByProvider().Select(usage => (usage.Provider.Id, usage.Usage.Tokens)));
    }

    [Fact]
    public async Task UsageAddsUpByTheAccountOfEachProviderLeavingOutSessionsWithoutAnAccountAsync()
    {
        using var tracked = new Tracked();
        var team = new AgentAccount("team", "Team");
        var personal = new AgentAccount("personal", "Personal");
        var first = await tracked.OpenAsync(Claude, team);
        var second = await tracked.OpenAsync(Claude, team);
        var other = await tracked.OpenAsync(Claude, personal);
        var sameIdElsewhere = await tracked.OpenAsync(Codex, team);
        var anonymous = await tracked.OpenAsync(Claude);

        await tracked.SeeAsync(
            Usage(first, new TokenUsage(100, 10, 0, 0, 0), new Cost(0.01m, "USD")),
            Usage(second, new TokenUsage(200, 20, 0, 0, 0), new Cost(0.02m, "USD")),
            Usage(other, new TokenUsage(5, 5, 0, 0, 0), new Cost(0.05m, "USD")),
            Usage(sameIdElsewhere, new TokenUsage(7, 7, 0, 0, 0), new Cost(0.07m, "USD")),
            Usage(anonymous, new TokenUsage(9, 9, 0, 0, 0), new Cost(0.09m, "USD")));

        Assert.Equal(
            [
                ("claude", personal, new TokenUsage(5, 5, 0, 0, 0)),
                ("claude", team, new TokenUsage(300, 30, 0, 0, 0)),
                ("codex", team, new TokenUsage(7, 7, 0, 0, 0)),
            ],
            tracked.Book.ByAccount().Select(usage => (usage.Provider.Id, usage.Account, usage.Usage.Tokens)));
    }

    [Fact]
    public async Task UsageAndLimitsAddUpByConnectionKeepingTwoConnectionsOfOneProviderApartAsync()
    {
        using var tracked = new Tracked();
        var work = new ConnectionName("work");
        var first = await tracked.OpenAsync(Claude, work, Option<AgentAccount>.None);
        var second = await tracked.OpenAsync(Claude, work, Option<AgentAccount>.None);
        var personal = await tracked.OpenAsync(Claude, new ConnectionName("personal"), Option<AgentAccount>.None);

        await tracked.SeeAsync(
            Usage(first, new TokenUsage(100, 10, 0, 0, 0), new Cost(0.01m, "USD")),
            Usage(second, new TokenUsage(200, 20, 0, 0, 0), new Cost(0.02m, "USD")),
            Limit(second, "5h", 0.2),
            Usage(personal, new TokenUsage(5, 5, 0, 0, 0), new Cost(0.05m, "USD")),
            Limit(personal, "5h", 0.8));

        Assert.Equal(
            [
                ("personal", "claude", new TokenUsage(5, 5, 0, 0, 0), 0.8),
                ("work", "claude", new TokenUsage(300, 30, 0, 0, 0), 0.2),
            ],
            tracked.Book.ByConnection().Select(usage => (usage.Connection.Value, usage.Provider.Id, usage.Usage.Tokens, Assert.Single(usage.Usage.Limits).UsedFraction)));
    }

    [Fact]
    public async Task UsageReportedWithoutCostAddsItsTokensAndCountsAsUnpricedAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Claude);

        await tracked.SeeAsync(
            Usage(session, new TokenUsage(100, 10, 0, 0, 0), new Cost(0.01m, "USD")),
            new UsageReported(session, TurnId.New(), new TokenUsage(40, 4, 0, 0, 0), Option<Cost>.None));

        var usage = Outcomes.Present(tracked.Book.OfSession(session));
        Assert.Equal(new TokenUsage(140, 14, 0, 0, 0), usage.Tokens);
        Assert.Equal([new Cost(0.01m, "USD")], usage.Costs);
        Assert.Equal(1, usage.UnpricedReports);
    }

    [Fact]
    public async Task TurnsAreCountedByOutcomeWithTheTimeFromTheirStartToTheirEndAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Claude);

        await TurnAsync(tracked, session, TurnOutcome.Finished, TimeSpan.FromSeconds(30));
        await TurnAsync(tracked, session, TurnOutcome.Finished, TimeSpan.FromSeconds(10));
        await TurnAsync(tracked, session, TurnOutcome.Interrupted, TimeSpan.FromSeconds(5));
        await TurnAsync(tracked, session, TurnOutcome.Failed, TimeSpan.FromSeconds(1));

        Assert.Equal(new TurnTally(2, 1, 1, TimeSpan.FromSeconds(46)), Outcomes.Present(tracked.Book.OfSession(session)).Turns);
    }

    [Fact]
    public async Task ATurnStartedOrCompletedTwiceCountsOnceAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Claude);
        var turn = TurnId.New();

        await tracked.SeeAsync(new TurnStarted(session, turn));
        tracked.Clock.Advance(TimeSpan.FromSeconds(20));
        await tracked.SeeAsync(new TurnStarted(session, turn), new TurnCompleted(session, turn, TurnOutcome.Finished));
        tracked.Clock.Advance(TimeSpan.FromSeconds(20));
        await tracked.SeeAsync(new TurnCompleted(session, turn, TurnOutcome.Failed));

        Assert.Equal(new TurnTally(1, 0, 0, TimeSpan.FromSeconds(20)), Outcomes.Present(tracked.Book.OfSession(session)).Turns);
        Assert.Single(tracked.Measurements.Of("avala.agent.turns"));
    }

    [Fact]
    public async Task EachLimitWindowKeepsItsLatestReadingAsync()
    {
        using var tracked = new Tracked();
        var job = JobId.New();
        var first = await tracked.OpenAsync(Claude, job);
        var resumed = await tracked.OpenAsync(Claude, job);

        await tracked.SeeAsync(Limit(resumed, "5h", 0.10), Limit(first, "7d", 0.40));
        tracked.Clock.Advance(TimeSpan.FromMinutes(1));
        await tracked.SeeAsync(Limit(first, "5h", 0.25));

        Assert.Equal(
            [new UsageLimit("5h", 0.25, Option<DateTimeOffset>.None), new UsageLimit("7d", 0.40, Option<DateTimeOffset>.None)],
            Outcomes.Present(tracked.Book.OfJob(job)).Limits);
        Assert.Equal([0.10], Outcomes.Present(tracked.Book.OfSession(resumed)).Limits.Select(limit => limit.UsedFraction));
    }

    [Fact]
    public async Task ASessionWhoseProviderWasNeverAnnouncedIsTrackedOutsideEveryProviderAndConnectionAsync()
    {
        using var tracked = new Tracked();
        var session = SessionId.New();

        await tracked.SeeAsync(Usage(session, new TokenUsage(100, 10, 0, 0, 0), new Cost(0.01m, "USD")));

        Assert.Equal(new TokenUsage(100, 10, 0, 0, 0), Outcomes.Present(tracked.Book.OfSession(session)).Tokens);
        Assert.Empty(tracked.Book.ByProvider());
        Assert.Empty(tracked.Book.ByConnection());
    }

    [Fact]
    public async Task UnknownSessionsAndJobsHaveNoUsageAsync()
    {
        using var tracked = new Tracked();
        await tracked.OpenAsync(Claude, JobId.New());

        Assert.True(tracked.Book.OfSession(SessionId.New()).IsNone);
        Assert.True(tracked.Book.OfJob(JobId.New()).IsNone);
    }

    [Fact]
    public async Task EveryUsageOrLimitRecordedIsAnnouncedWithTheJobOfItsSessionAsync()
    {
        using var tracked = new Tracked();
        var job = JobId.New();
        var working = await tracked.OpenAsync(Claude, job);
        var loose = await tracked.OpenAsync(Codex);

        await tracked.SeeAsync(
            new TurnStarted(working, TurnId.New()),
            Usage(working, new TokenUsage(100, 10, 0, 0, 0), new Cost(0.01m, "USD")),
            Limit(working, "5h", 0.5),
            Usage(loose, new TokenUsage(1, 1, 0, 0, 0), new Cost(0.01m, "USD")));

        Assert.Equal(
            [new UsageRecorded(working, job), new UsageRecorded(working, job), new UsageRecorded(loose, Option<JobId>.None)],
            tracked.Bus.Published);
    }

    private static async Task TurnAsync(Tracked tracked, SessionId session, TurnOutcome outcome, TimeSpan duration)
    {
        var turn = TurnId.New();
        await tracked.SeeAsync(new TurnStarted(session, turn));
        tracked.Clock.Advance(duration);
        await tracked.SeeAsync(new TurnCompleted(session, turn, outcome));
    }

    private static UsageReported Usage(SessionId session, TokenUsage tokens, Cost cost) => new(session, TurnId.New(), tokens, cost);

    private static LimitReported Limit(SessionId session, string window, double used) =>
        new(session, TurnId.New(), new UsageLimit(window, used, Option<DateTimeOffset>.None));
}
