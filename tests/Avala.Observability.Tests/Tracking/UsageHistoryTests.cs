using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Tracking;
using Avala.Observability.Usage;
using Avala.Sdk;
using Avala.Testing;
using static Avala.Observability.Tests.Tracking.Tracked;

namespace Avala.Observability.Tests.Tracking;

public sealed class UsageHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryReportLimitAndCountedTurnIsStoredAsAFactOfItsAttributedSessionAsync()
    {
        using var tracked = new Tracked();
        var job = JobId.New();
        var session = await tracked.OpenAsync(Claude, job);
        var turn = TurnId.New();

        await tracked.SeeAsync(new TurnStarted(session, turn), Usage(session, 100, "USD"), Limit(session, "5h", 0.5));
        tracked.Clock.Advance(TimeSpan.FromSeconds(30));
        await tracked.SeeAsync(new TurnCompleted(session, turn, TurnOutcome.Finished), new TurnCompleted(session, turn, TurnOutcome.Failed));

        Assert.Equal(
            [
                (FactKind.Usage, Nine, 100L, Option<UsageLimit>.None, Option<TurnOutcome>.None, TimeSpan.Zero),
                (FactKind.Limit, Nine, 0L, new UsageLimit("5h", 0.5, Option<DateTimeOffset>.None), Option<TurnOutcome>.None, TimeSpan.Zero),
                (FactKind.Turn, Nine.AddSeconds(30), 0L, Option<UsageLimit>.None, TurnOutcome.Finished, TimeSpan.FromSeconds(30)),
            ],
            tracked.Store.Facts.Select(fact => (fact.Kind, fact.At, fact.Tokens.Input, fact.Limit, fact.Outcome, fact.Duration)));
        Assert.All(tracked.Store.Facts, fact => Assert.Equal(session, fact.Session));
        var attributed = tracked.Store.Sessions[^1];
        Assert.Equal((Option<ProviderInfo>.Some(Claude), Option<JobId>.Some(job)), (attributed.Provider, attributed.Job));
    }

    [Fact]
    public async Task UsageOfEarlierRunsIsRestoredAndCountsAlongsideTheLiveSessionsAsync()
    {
        using var tracked = new Tracked();
        var job = JobId.New();
        var earlier = SessionId.New();
        tracked.Store.Earlier = new StoredUsage(
            [new SessionUsage(earlier).Attributed(Claude, Option<AgentAccount>.None, new ConnectionName("work"), job)],
            [UsageFact.Of(Usage(earlier, 100, "USD"), Nine.AddDays(-1)), UsageFact.Of(Limit(earlier, "5h", 0.9), Nine.AddDays(-1))]);

        await new UsageRestore(tracked.Book, tracked.Store).RunAsync(Cancellation);
        var live = await tracked.OpenAsync(Claude, job);
        await tracked.SeeAsync(Usage(live, 50, "USD"));

        var ofJob = Outcomes.Present(tracked.Book.OfJob(job));
        Assert.Equal((150L, new Cost(0.15m, "USD")), (ofJob.Tokens.Input, Assert.Single(ofJob.Costs)));
        Assert.Equal(0.9, Assert.Single(Assert.Single(tracked.Book.ByConnection(), used => used.Connection.Value == "work").Usage.Limits).UsedFraction);
        Assert.Single(tracked.Store.Facts);
    }

    [Fact]
    public async Task AWindowAddsUpOnlyTheFactsInsideItByProviderAndConnectionAsync()
    {
        using var tracked = new Tracked();
        var morning = await tracked.OpenAsync(Claude, new ConnectionName("work"), Option<AgentAccount>.None);
        var later = await tracked.OpenAsync(Codex, new ConnectionName("personal"), Option<AgentAccount>.None);
        await tracked.SeeAsync(Usage(morning, 100, "USD"));
        tracked.Clock.Advance(TimeSpan.FromHours(2));
        await tracked.SeeAsync(Usage(later, 7, "EUR"));

        var period = await new UsageHistory(tracked.Store).WithinAsync(Nine.AddHours(1), Nine.AddHours(3), Cancellation);

        Assert.Equal((Nine.AddHours(1), Nine.AddHours(3)), (period.From, period.To));
        Assert.Equal((7L, new Cost(0.007m, "EUR")), (period.Usage.Tokens.Input, Assert.Single(period.Usage.Costs)));
        Assert.Equal(["codex"], period.ByProvider.Select(used => used.Provider.Id));
        Assert.Equal(["personal"], period.ByConnection.Select(used => used.Connection.Value));
    }

    [Fact]
    public async Task DailyUsageSplitsTheFactsByTheDaysOfATimeZoneAsync()
    {
        using var tracked = new Tracked();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Avala+2", TimeSpan.FromHours(2), "Avala+2", "Avala+2");
        var session = await tracked.OpenAsync(Claude);
        tracked.Clock.Advance(TimeSpan.FromHours(12.5));
        await tracked.SeeAsync(Usage(session, 1, "USD"));
        tracked.Clock.Advance(TimeSpan.FromHours(1));
        await tracked.SeeAsync(Usage(session, 2, "USD"), Usage(session, 3, "USD"));

        var days = await new UsageHistory(tracked.Store).DailyAsync(new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 11), zone, Cancellation);

        Assert.Equal(
            [
                (new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.FromHours(2)), 1L),
                (new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(2)), 5L),
                (new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.FromHours(2)), 0L),
            ],
            days.Select(day => (day.From, day.Usage.Tokens.Input)));
        Assert.Equal(days[1].From.AddDays(1), days[1].To);
    }

    private static UsageReported Usage(SessionId session, long input, string currency) =>
        new(session, TurnId.New(), new TokenUsage(input, 0, 0, 0, 0), new Cost(input / 1000m, currency));

    private static LimitReported Limit(SessionId session, string window, double used) =>
        new(session, TurnId.New(), new UsageLimit(window, used, Option<DateTimeOffset>.None));
}
