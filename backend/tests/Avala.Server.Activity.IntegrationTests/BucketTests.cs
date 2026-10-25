using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using static Avala.Server.Activity.IntegrationTests.Fleet;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class BucketTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Desktop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    private static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000100");

    private static readonly Guid Other = Guid.Parse("0199f6a0-0000-7000-8000-000000000200");

    private readonly Fleet fleet = new(database);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UsageIsKeptPerJobModelAndQuarterAsync()
    {
        await database.BeginAsync();

        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Usage(Guid.NewGuid(), Job, 10, 0.1m, Noon.AddMinutes(5), "opus"),
            Usage(Guid.NewGuid(), Job, 20, 0.2m, Noon.AddMinutes(10), "opus"),
            Usage(Guid.NewGuid(), Job, 40, 0.4m, Noon.AddMinutes(20), "opus"));

        Assert.Equal(
            [$"{Noon:HH:mm} opus 30 0.3", $"{Noon.AddMinutes(15):HH:mm} opus 40 0.4"],
            Shown(await fleet.QuartersAsync()));
    }

    [Fact]
    public async Task ALaterBatchAddsToTheExistingQuarterAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, 0.1m, Noon.AddMinutes(1), "opus"));

        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 5, 0.05m, Noon.AddMinutes(2), "opus"));

        Assert.Equal([$"{Noon:HH:mm} opus 15 0.15"], Shown(await fleet.QuartersAsync()));
    }

    [Fact]
    public async Task UsageOfTwoModelsInOneQuarterIsKeptApartAsync()
    {
        await database.BeginAsync();

        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Usage(Guid.NewGuid(), Job, 10, 0.1m, Noon.AddMinutes(1), "opus"),
            Usage(Guid.NewGuid(), Job, 3, 0.03m, Noon.AddMinutes(2), "sonnet"),
            Usage(Guid.NewGuid(), Job, 1, at: Noon.AddMinutes(3)));

        Assert.Equal(
            [$"{Noon:HH:mm}  1 -", $"{Noon:HH:mm} opus 10 0.1", $"{Noon:HH:mm} sonnet 3 0.03"],
            Shown(await fleet.QuartersAsync()));
    }

    [Fact]
    public async Task UsageOutsideAJobIsKeptPerInstanceAndQuarterAsync()
    {
        await database.BeginAsync();

        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), job: null, 7, 1m, Noon.AddMinutes(31)));

        var bucket = Assert.Single(await fleet.QuartersAsync());
        Assert.Equal((QuarterUsage.OutsideAnyJob, Noon.AddMinutes(30), 7m), (bucket.JobId, bucket.Quarter, bucket.Tokens.Input));
    }

    [Fact]
    public async Task EachInstanceKeepsItsOwnBucketsOutsideAnyJobAsync()
    {
        await database.BeginAsync();
        var laptop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000b");

        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), job: null, 7, at: Noon.AddMinutes(1), model: "opus"));
        await fleet.IngestAsync(laptop, "laptop", Usage(Guid.NewGuid(), job: null, 3, at: Noon.AddMinutes(2), model: "opus"));

        Assert.Equal(
            [(Desktop, 7m), (laptop, 3m)],
            (await fleet.QuartersAsync()).Select(bucket => (bucket.InstanceId, bucket.Tokens.Input)).OrderByDescending(bucket => bucket.Input));
    }

    [Fact]
    public async Task UsageWithAnEmptyJobIdIsRejectedAndAddsNoBucketAsync()
    {
        await database.BeginAsync();

        var receipt = await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Guid.Empty, 7, 1m, Noon));

        Assert.Equal(("the job id is empty", 0), (Assert.Single(receipt.Rejected).Reason, (await fleet.QuartersAsync()).Count));
    }

    [Fact]
    public async Task ARefusedUsageAddsNoBucketAsync()
    {
        await database.BeginAsync();

        var receipt = await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 7, 0.1234567m, Noon));

        Assert.Equal((1, 0), (receipt.Rejected.Count, (await fleet.QuartersAsync()).Count));
    }

    [Fact]
    public async Task ABucketJoinsItsJobsRepositoryAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 7, at: Noon.AddMinutes(5), model: "opus"));
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon));

        await using var context = database.Context();
        var repository = await context.Quarters
            .Join(context.Jobs, usage => new { usage.InstanceId, usage.JobId }, job => new { job.InstanceId, job.JobId }, (usage, job) => job.Repository)
            .SingleAsync(Cancellation);

        Assert.Equal(AvalaRemote, repository?.Value);
    }

    [Fact]
    public async Task BucketSumsEqualTheJobAndInstanceSumsAsync()
    {
        await database.BeginAsync();
        string[] models = ["opus", "sonnet"];
        Guid?[] owners = [Job, Other, null];
        UsageReportedEvent[] usages =
        [
            .. Enumerable.Range(1, 30).Select(number => Usage(
                Guid.NewGuid(),
                owners[number % owners.Length],
                number,
                number / 100m,
                Noon.AddMinutes(number * 2),
                models[number % models.Length])),
        ];
        var refused = Usage(Guid.NewGuid(), Job, 999, 0.1234567m, Noon.AddMinutes(1), "opus");
        IIncomingEvent[][] batches = [.. Enumerable.Range(0, 8).Select(index => usages.Skip(index * 3).Take(6).Append(refused).ToArray<IIncomingEvent>())];

        await Task.WhenAll(batches.Select(events => Task.Run(() => fleet.IngestAsync(Desktop, "desktop", events), Cancellation)));

        await using var context = database.Context();
        var buckets = await context.Quarters.AsNoTracking().ToListAsync(Cancellation);
        var jobs = await context.Jobs.AsNoTracking().ToListAsync(Cancellation);
        var instance = await context.Instances.AsNoTracking().SingleAsync(Cancellation);
        Assert.Equal(
            [.. jobs.Select(job => (job.JobId, job.Tokens.Input, job.Costs.Sum(cost => cost.Amount))).Append((QuarterUsage.OutsideAnyJob, instance.Tokens.Input, instance.Costs.Sum(cost => cost.Amount))).OrderBy(total => total.Item1)],
            buckets.GroupBy(bucket => bucket.JobId).Select(group => (group.Key, group.Sum(bucket => bucket.Tokens.Input), group.Sum(bucket => bucket.Costs.Sum(cost => cost.Amount)))).OrderBy(total => total.Key));
    }

    private static IReadOnlyList<string> Shown(IReadOnlyList<QuarterUsage> buckets) =>
        [
            .. buckets.Select(bucket =>
                $"{bucket.Quarter:HH:mm} {bucket.Model} {bucket.Tokens.Input} {(bucket.Costs.Count == 0 ? "-" : bucket.Costs.Sum(cost => cost.Amount).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))}"),
        ];
}
