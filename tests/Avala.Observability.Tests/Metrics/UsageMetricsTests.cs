using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Observability.Tests.Tracking;
using Avala.Sdk;

namespace Avala.Observability.Tests.Metrics;

public sealed class UsageMetricsTests
{
    [Fact]
    public async Task ReportedUsageIsMeasuredAsTokensByTypeAndCostByCurrencyForItsProviderAndConnectionAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Tracked.Claude, new ConnectionName("work"), Option<AgentAccount>.None);

        await tracked.SeeAsync(new UsageReported(session, TurnId.New(), new TokenUsage(100, 10, 50, 5, 2), new Cost(0.25m, "USD")));

        Assert.Equal(
            [
                new Measured("avala.agent.tokens", 100, "avala.connection=work,avala.provider=claude,avala.token.type=input"),
                new Measured("avala.agent.tokens", 10, "avala.connection=work,avala.provider=claude,avala.token.type=output"),
                new Measured("avala.agent.tokens", 50, "avala.connection=work,avala.provider=claude,avala.token.type=cache_read"),
                new Measured("avala.agent.tokens", 5, "avala.connection=work,avala.provider=claude,avala.token.type=cache_write"),
                new Measured("avala.agent.tokens", 2, "avala.connection=work,avala.provider=claude,avala.token.type=reasoning"),
            ],
            tracked.Measurements.Of("avala.agent.tokens"));
        Assert.Equal(
            [new Measured("avala.agent.cost", 0.25, "avala.connection=work,avala.currency=USD,avala.provider=claude")],
            tracked.Measurements.Of("avala.agent.cost"));
    }

    [Fact]
    public async Task AFinishedTurnIsMeasuredByOutcomeWithItsDurationInSecondsAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Tracked.Claude);
        var turn = TurnId.New();

        await tracked.SeeAsync(new TurnStarted(session, turn));
        tracked.Clock.Advance(TimeSpan.FromSeconds(42));
        await tracked.SeeAsync(new TurnCompleted(session, turn, TurnOutcome.Interrupted));

        const string Tags = "avala.connection=claude,avala.provider=claude,avala.turn.outcome=Interrupted";
        Assert.Equal([new Measured("avala.agent.turns", 1, Tags)], tracked.Measurements.Of("avala.agent.turns"));
        Assert.Equal([new Measured("avala.agent.turn.duration", 42, Tags)], tracked.Measurements.Of("avala.agent.turn.duration"));
    }

    [Fact]
    public async Task AReportedLimitIsMeasuredAsTheFractionUsedOfItsWindowAsync()
    {
        using var tracked = new Tracked();
        var session = await tracked.OpenAsync(Tracked.Claude);

        await tracked.SeeAsync(new LimitReported(session, TurnId.New(), new UsageLimit("5h", 0.3, Option<DateTimeOffset>.None)));

        Assert.Equal(
            [new Measured("avala.agent.limit.used", 0.3, "avala.connection=claude,avala.limit.window=5h,avala.provider=claude")],
            tracked.Measurements.Of("avala.agent.limit.used"));
    }
}
