using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Microsoft.EntityFrameworkCore;
using static Avala.Server.Activity.IntegrationTests.Fleet;
using Model = Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class IngestionTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Desktop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    private static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000100");

    private readonly Fleet fleet = new(database);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSameEventSentTwiceCountsOnceAsync()
    {
        await database.BeginAsync();
        var usage = Usage(Guid.NewGuid(), Job, 100, 0.5m);

        await fleet.IngestAsync(Desktop, "desktop", usage);
        var again = await fleet.IngestAsync(Desktop, "desktop", usage);

        var job = await fleet.JobAsync(Desktop, Job);
        Assert.Equal((usage.Id.ToString(), 100m, 0.5m), (string.Join(", ", again.Duplicates), job.Tokens.Input, job.Costs.Single().Amount));
    }

    [Fact]
    public async Task ARefusedEventIsRejectedAndTheOthersAreSavedAsync()
    {
        await database.BeginAsync();
        var valid = Usage(Guid.NewGuid(), Job, 100, 0.5m);
        var tooPrecise = Usage(Guid.NewGuid(), Job, 50, 0.1234567m);
        var credentialed = Submitted(Guid.NewGuid(), Guid.NewGuid(), "remote:rick@github.com/rick/avala", Noon);
        var withNul = new JobSubmittedEvent(Guid.NewGuid(), Noon, Guid.NewGuid(), AvalaRemote, null, "op\0us", null);

        var receipt = await fleet.IngestAsync(Desktop, "desktop", valid, tooPrecise, credentialed, withNul);

        await using var context = database.Context();
        var ledger = await context.ReceivedEvents.Select(received => received.EventId).ToListAsync(Cancellation);
        var job = await fleet.JobAsync(Desktop, Job);
        Assert.Equal(
            ("a cost has at most 6 decimals, the repository key carries credentials, a model name or effort holds a control character or broken text", 100m, valid.Id.ToString()),
            (string.Join(", ", receipt.Rejected.Select(rejected => rejected.Reason)), job.Tokens.Input, string.Join(", ", ledger)));
    }

    [Fact]
    public async Task EventsOfABatchAreAppliedInTheirOrderAsync()
    {
        await database.BeginAsync();
        var elsewhere = Submitted(Guid.NewGuid(), Job, "remote:github.com/rick/elsewhere", Noon.AddMinutes(5));

        var receipt = await fleet.IngestAsync(
            Desktop,
            "desktop",
            elsewhere,
            Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon.AddMinutes(4)),
            Progressed(Guid.NewGuid(), Job, JobStatus.Running, Noon.AddDays(2)),
            Progressed(Guid.NewGuid(), Job, JobStatus.Checking, Noon.AddDays(3)),
            Held(Guid.NewGuid(), Job, HoldReason.Stalled, Noon.AddMinutes(1)));

        var job = await fleet.JobAsync(Desktop, Job);
        Assert.Equal(
            (AvalaRemote, Model.JobStatus.Checking, (Model.HoldReason?)null, elsewhere.Id, Noon.AddMinutes(1), Noon.AddHours(1)),
            (job.Repository?.Value, job.Status, job.VisibleHold, Assert.Single(receipt.Rejected).Id, job.FirstActivityAt, job.LastActivityAt));
    }

    [Fact]
    public async Task UsageOutsideAJobIsSavedOnTheInstanceAsync()
    {
        await database.BeginAsync();

        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), job: null, 7, 1m));

        await using var context = database.Context();
        var instance = await context.Instances.AsNoTracking().SingleAsync(Cancellation);
        Assert.Equal(("desktop", 7m, 1m, 0), (instance.Name, instance.Tokens.Input, instance.Costs.Single().Amount, await context.Jobs.CountAsync(Cancellation)));
    }

    [Fact]
    public async Task AnEventWithALocalOffsetIsSavedAsync()
    {
        await database.BeginAsync();

        var receipt = await fleet.IngestAsync(
            Desktop,
            "desktop",
            Progressed(Guid.NewGuid(), Job, JobStatus.Running, new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(9))));

        Assert.Equal(
            (1, new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero)),
            (receipt.Accepted.Count, (await fleet.JobAsync(Desktop, Job)).StatusAt));
    }

    [Fact]
    public async Task LargeSumsAreStoredWithoutOverflowAsync()
    {
        await database.BeginAsync();
        IIncomingEvent[] events = [.. Enumerable.Range(0, 500).Select(_ => Usage(Guid.NewGuid(), Job, 1_000_000_000_000, 1_000_000m))];

        await fleet.IngestAsync(Desktop, "desktop", events);

        var job = await fleet.JobAsync(Desktop, Job);
        Assert.Equal((500_000_000_000_000m, 500_000_000m), (job.Tokens.Input, job.Costs.Single().Amount));
    }
}
