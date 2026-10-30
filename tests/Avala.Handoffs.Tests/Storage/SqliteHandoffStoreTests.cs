using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Storage;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.Storage;

public sealed class SqliteHandoffStoreTests
{
    private static readonly ConnectionName Work = new("work");

    private static readonly ConnectionName Personal = new("personal");

    private static readonly DateTimeOffset Nine = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandoffsAndMarksSurviveAReopeningInTheOrderTheyHappenedAsync()
    {
        await using var folder = new TemporaryFolder();
        var job = JobId.New();
        var first = new HandoffRecord(job, 1, Work, Personal, new LimitReason(Work, "5h", 0.91, 0.9), Nine)
        {
            Spent = [new Cost(1.84m, "USD")],
            Tokens = 412_880,
            Model = new ModelFallback(new ModelChoice("opus", "high"), new ModelChoice("sonnet", "high")),
        };
        var second = new HandoffRecord(job, 2, Personal, Work, Option<LimitReason>.None, Nine.AddHours(2));
        var capped = new RelayMark(job, RelayMarkKind.Capped, Work, Nine) { Why = new LimitReason(Work, "5h", 0.91, 0.9), Until = Nine.AddHours(5) };
        var resumed = new RelayMark(job, RelayMarkKind.Resumed, Personal, Nine.AddHours(5));
        await using (var store = new SqliteHandoffStore(new AvalaPaths(folder.Path)))
        {
            await store.RunAsync(Cancellation);
            await store.AddAsync(first, Cancellation);
            await store.MarkAsync(capped, Cancellation);
            await store.AddAsync(second, Cancellation);
            await store.MarkAsync(resumed, Cancellation);
        }

        await using var reopened = new SqliteHandoffStore(new AvalaPaths(folder.Path));
        var handoffs = await reopened.HandoffsAsync(Cancellation);

        Assert.Equal([first with { Spent = [] }, second], [.. handoffs.Select(handoff => handoff with { Spent = [] })]);
        Assert.Equal([new Cost(1.84m, "USD")], handoffs[0].Spent);
        Assert.Equal([capped, resumed], await reopened.MarksAsync(Cancellation));
    }

    [Fact]
    public async Task AJobKeepsOneWaitWhichSurvivesAReopeningAsItsLatestVersionAsync()
    {
        await using var folder = new TemporaryFolder();
        var later = new KeptWait(JobId.New(), "Fix the docs", Nine);
        var earlier = new KeptWait(JobId.New(), Option<string>.None, Nine.AddMinutes(-30));
        await using (var store = new SqliteHandoffStore(new AvalaPaths(folder.Path)))
        {
            await store.KeepAsync(later with { Pending = "Fix the calculator" }, Cancellation);
            await store.KeepAsync(earlier, Cancellation);
            await store.KeepAsync(later, Cancellation);
        }

        await using var reopened = new SqliteHandoffStore(new AvalaPaths(folder.Path));

        Assert.Equal([earlier, later], await reopened.WaitsAsync(Cancellation));
    }

    [Fact]
    public async Task ADroppedWaitIsGoneAfterAReopeningAsync()
    {
        await using var folder = new TemporaryFolder();
        var dropped = new KeptWait(JobId.New(), "Fix the docs", Nine);
        var kept = new KeptWait(JobId.New(), "Fix the calculator", Nine.AddMinutes(1));
        await using (var store = new SqliteHandoffStore(new AvalaPaths(folder.Path)))
        {
            await store.KeepAsync(dropped, Cancellation);
            await store.KeepAsync(kept, Cancellation);
            await store.DropAsync(dropped.Job, Cancellation);
        }

        await using var reopened = new SqliteHandoffStore(new AvalaPaths(folder.Path));

        Assert.Equal([kept], await reopened.WaitsAsync(Cancellation));
    }
}
