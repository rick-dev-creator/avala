using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Storage;
using Avala.Observability.Usage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Observability.Tests.Storage;

public sealed class SqliteUsageStoreTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly ProviderInfo Claude = new("claude", "Claude Code");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UsageSurvivesAReopeningAndEarlierRunsLeaveOutWhatThisRunWroteAsync()
    {
        using var folder = new TemporaryFolder();
        var earlier = Attributed(SessionId.New(), JobId.New());
        var facts = new[]
        {
            UsageFact.Of(new UsageReported(earlier.Session, TurnId.New(), new TokenUsage(1, 2, 3, 4, 5), new Cost(0.25m, "USD")), Nine),
            UsageFact.Of(new LimitReported(earlier.Session, TurnId.New(), new UsageLimit("5h", 0.5, Nine.AddHours(5))), Nine.AddMinutes(1)),
            UsageFact.Turn(earlier.Session, TurnOutcome.Interrupted, TimeSpan.FromSeconds(12), Nine.AddMinutes(2)),
        };
        await using (var first = new SqliteUsageStore(new AvalaPaths(folder.Path)))
        {
            await first.KeepSessionAsync(new SessionUsage(earlier.Session), Cancellation);
            await first.KeepSessionAsync(earlier, Cancellation);

            foreach (var fact in facts)
            {
                await first.RecordAsync(fact, Cancellation);
            }
        }

        await using var second = new SqliteUsageStore(new AvalaPaths(folder.Path));
        var live = Attributed(SessionId.New(), JobId.New());
        await second.KeepSessionAsync(live, Cancellation);
        await second.RecordAsync(UsageFact.Turn(live.Session, TurnOutcome.Finished, TimeSpan.FromSeconds(1), Nine), Cancellation);

        var stored = await second.EarlierRunsAsync(Cancellation);

        var session = Assert.Single(stored.Sessions);
        Assert.Equal(
            (earlier.Session, earlier.Provider, earlier.Account, earlier.Connection, earlier.Job, earlier.Opened),
            (session.Session, session.Provider, session.Account, session.Connection, session.Job, session.Opened));
        Assert.Equal(facts, stored.Facts);
    }

    [Fact]
    public async Task AWindowHoldsTheFactsInsideItWithTheirSessionsAsync()
    {
        using var folder = new TemporaryFolder();
        await using var store = new SqliteUsageStore(new AvalaPaths(folder.Path));
        var before = Attributed(SessionId.New(), JobId.New());
        var inside = Attributed(SessionId.New(), JobId.New());
        await store.KeepSessionAsync(before, Cancellation);
        await store.KeepSessionAsync(inside, Cancellation);
        await store.RecordAsync(UsageFact.Turn(before.Session, TurnOutcome.Finished, TimeSpan.Zero, Nine.AddTicks(-1)), Cancellation);
        await store.RecordAsync(UsageFact.Turn(inside.Session, TurnOutcome.Finished, TimeSpan.Zero, Nine), Cancellation);
        await store.RecordAsync(UsageFact.Turn(inside.Session, TurnOutcome.Failed, TimeSpan.Zero, Nine.AddHours(1)), Cancellation);

        var window = await store.WithinAsync(Nine, Nine.AddHours(1), Cancellation);

        Assert.Equal([inside.Session], window.Sessions.Select(session => session.Session));
        Assert.Equal([(inside.Session, Nine)], window.Facts.Select(fact => (fact.Session, fact.At)));
    }

    private static SessionUsage Attributed(SessionId session, JobId job) =>
        new SessionUsage(session).Attributed(Claude, new AgentAccount("team", "Team"), new ConnectionName("work"), job).OpenedAt(Nine.AddMinutes(-5));
}
