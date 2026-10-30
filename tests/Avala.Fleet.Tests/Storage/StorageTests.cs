using Avala.Fleet.Connecting;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Fleet.FleetFiles;
using Avala.Fleet.Storage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Fleet.Tests.Storage;

public sealed class SqliteFactStoreTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryFolder data = new();

    private readonly SqliteFactStore store;

    public SqliteFactStoreTests() => store = new SqliteFactStore(new AvalaPaths(data.Path));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FactsAreDueInTheOrderTheyWereAppendedWithTheirWireBodies()
    {
        await store.RunAsync(Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(2), Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(1), Cancellation);
        await store.AppendAsync("another|scope", Fact(3), Cancellation);

        var due = await store.DueAsync(Fleets.Scope, Noon, 10, Cancellation);

        Assert.Equal([Id(2), Id(1)], due.Select(fact => fact.Id));
        Assert.Contains("\"kind\":\"jobProgressed\"", due[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASettledReceiptDeletesRejectsAndDefersWithItsBackoffAndSurvivesARestart()
    {
        await store.RunAsync(Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(1), Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(2), Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(3), Cancellation);
        var plan = ReceiptPlan.Of([Id(1), Id(2), Id(3)], new Receipt([Id(1)], [], [new ReceiptEntry(Id(2).ToString(), "unknown kind 'x'")], [new ReceiptEntry(Id(3).ToString(), "the job id is empty")]));

        await store.SettleAsync(Fleets.Scope, plan, Noon, Cancellation);
        await using var reopened = new SqliteFactStore(new AvalaPaths(data.Path));
        await reopened.RunAsync(Cancellation);
        var summary = await reopened.SummaryAsync(Fleets.Scope, Cancellation);

        Assert.Equal(
            (1, Noon, "the job id is empty", 0, Noon + Backoff.After(1)),
            (summary.Waiting, summary.LastDelivery.Match(at => at, () => DateTimeOffset.MinValue), Assert.Single(summary.Rejections).Reason, (await reopened.DueAsync(Fleets.Scope, Noon, 10, Cancellation)).Count, (await reopened.NextDueAsync(Fleets.Scope, Cancellation)).Match(at => at, () => DateTimeOffset.MinValue)));
    }

    [Fact]
    public async Task AnExpiredDeferredFactIsDroppedAndCounted()
    {
        await store.RunAsync(Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(1), Cancellation);
        await store.SettleAsync(Fleets.Scope, ReceiptPlan.Of([Id(1)], new Receipt([], [], [new ReceiptEntry(Id(1).ToString(), "unknown kind 'x'")], [])), Noon, Cancellation);

        var dropped = await store.ExpireAsync(Fleets.Scope, Noon.AddDays(31), Cancellation);

        Assert.Equal((1, 1, 0), (dropped, (await store.SummaryAsync(Fleets.Scope, Cancellation)).Dropped, (await store.SummaryAsync(Fleets.Scope, Cancellation)).Waiting));
    }

    [Fact]
    public async Task AnnouncedJobsAreKeptPerScope()
    {
        await store.RunAsync(Cancellation);
        var job = Guid.NewGuid();

        await store.AnnounceAsync(Fleets.Scope, job, keyless: false, Cancellation);

        Assert.Equal((true, false), (await store.IsAnnouncedAsync(Fleets.Scope, job, Cancellation), await store.IsAnnouncedAsync("another|scope", job, Cancellation)));
    }

    [Fact]
    public async Task KeepingOneScopeForgetsTheOthers()
    {
        await store.RunAsync(Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(1), Cancellation);
        await store.AppendAsync("another|scope", Fact(2), Cancellation);
        await store.AnnounceAsync("another|scope", Guid.NewGuid(), keyless: true, Cancellation);

        await store.KeepOnlyAsync(Fleets.Scope, Cancellation);

        Assert.Equal((1, 0), ((await store.SummaryAsync(Fleets.Scope, Cancellation)).Waiting, (await store.SummaryAsync("another|scope", Cancellation)).Waiting));
    }

    [Fact]
    public async Task TheLatestStampIsTheNewestFactsTime()
    {
        await store.RunAsync(Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(1) with { OccurredAt = Noon.AddHours(2) }, Cancellation);
        await store.AppendAsync(Fleets.Scope, Fact(2), Cancellation);

        Assert.Equal(Noon.AddHours(2), await store.LatestStampAsync(Cancellation));
    }

    public async ValueTask DisposeAsync()
    {
        await store.DisposeAsync();
        await data.DisposeAsync();
    }

    private static Guid Id(int number) => Guid.Parse($"0199f6a0-0000-7000-8000-{number:D12}");

    private static Fact Fact(int number) => new(Id(number), Noon, new JobProgressedFact(Guid.NewGuid(), "running"));
}

public sealed class FleetFileStoreTests : IAsyncDisposable
{
    private readonly TemporaryFolder data = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSettingsAreWrittenReadAndDeletedWithoutAKey()
    {
        await using var file = new FleetFileStore(new AvalaPaths(data.Path));

        await file.WriteAsync(Fleets.Settings(), Cancellation);
        var read = await file.ReadAsync(Cancellation);
        var text = await File.ReadAllTextAsync(file.Path, Cancellation);
        await file.DeleteAsync(Cancellation);

        Assert.Equal((Fleets.Settings(), false, false), (read.Match(found => found, () => Fleets.Settings(enabled: false)), text.Contains("avk_", StringComparison.Ordinal), File.Exists(file.Path)));
    }

    [Fact]
    public async Task TheFileHoldsOnlyItsFiveSettings()
    {
        await using var file = new FleetFileStore(new AvalaPaths(data.Path));

        await file.WriteAsync(Fleets.Settings(), Cancellation);

        Assert.Equal(
            ["enabled", "instance", "instanceName", "keyRefused", "server"],
            System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file.Path, Cancellation))!.AsObject().Select(property => property.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnUnreadableFileIsNoSettings()
    {
        await using var file = new FleetFileStore(new AvalaPaths(data.Path));
        await File.WriteAllTextAsync(file.Path, "{ not json", Cancellation);

        Assert.True((await file.ReadAsync(Cancellation)).IsNone);
    }

    public ValueTask DisposeAsync() => data.DisposeAsync();
}
