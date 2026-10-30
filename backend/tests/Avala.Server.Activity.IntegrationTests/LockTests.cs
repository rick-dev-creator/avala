using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Infrastructure.Storage;
using Npgsql;
using static Avala.Server.Activity.IntegrationTests.Fleet;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class LockTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Desktop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    private static readonly Guid Laptop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000b");

    private static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000100");

    private readonly Fleet fleet = new(database);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABatchOfAnInstanceWaitsWhileItsLockIsHeldAsync()
    {
        await database.BeginAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await using var holding = await Fleet.HoldLockAsync(connection, Desktop);

        var batch = Task.Run(() => fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10)), Cancellation);
        var waited = await fleet.WaitsForLockAsync(Desktop);
        var completedWhileHeld = batch.IsCompleted;
        await holding.CommitAsync(Cancellation);
        await BoundedAsync(batch);

        Assert.Equal((true, false, 10m), (waited, completedWhileHeld, (await fleet.JobAsync(Desktop, Job)).Tokens.Input));
    }

    [Fact]
    public async Task ABatchSeesWhatWasSavedWhileItWaitedAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 1));
        var seenElsewhere = Usage(Guid.NewGuid(), Job, 10);
        var fresh = Usage(Guid.NewGuid(), Job, 20);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await using var holding = await Fleet.HoldLockAsync(connection, Desktop);

        var batch = Task.Run(() => fleet.IngestAsync(Desktop, "desktop", seenElsewhere, fresh), Cancellation);
        Assert.True(await fleet.WaitsForLockAsync(Desktop));
        await using (var saving = new NpgsqlCommand(
            $"""
            INSERT INTO {ActivityDbContext.Schema}.received_events (instance_id, event_id, received_at) VALUES (@instance, @event, now());
            UPDATE {ActivityDbContext.Schema}.jobs SET input_tokens = input_tokens + 10 WHERE instance_id = @instance AND job_id = @job;
            """,
            connection,
            holding))
        {
            saving.Parameters.AddWithValue("instance", Desktop);
            saving.Parameters.AddWithValue("event", seenElsewhere.Id);
            saving.Parameters.AddWithValue("job", Job);
            await saving.ExecuteNonQueryAsync(Cancellation);
        }

        await holding.CommitAsync(Cancellation);
        var receipt = await BoundedAsync(batch);

        Assert.Equal(
            (seenElsewhere.Id.ToString(), fresh.Id.ToString(), 31m),
            (string.Join(", ", receipt.Duplicates), string.Join(", ", receipt.Accepted), (await fleet.JobAsync(Desktop, Job)).Tokens.Input));
    }

    [Fact]
    public async Task ConcurrentBatchesOfOneInstanceLoseNoUsageAsync()
    {
        await database.BeginAsync();
        UsageReportedEvent[] usages = [.. Enumerable.Range(1, 24).Select(number => Usage(Guid.NewGuid(), number % 2 == 0 ? Job : Guid.NewGuid(), number, number / 100m))];
        IIncomingEvent[][] batches = [.. Enumerable.Range(0, 8).Select(index => usages.Skip(index * 3).Take(5).ToArray<IIncomingEvent>())];

        await Task.WhenAll(batches.Select(events => Task.Run(() => fleet.IngestAsync(Desktop, "desktop", events), Cancellation)));

        var repositories = await fleet.RepositoriesAsync();
        var job = await fleet.JobAsync(Desktop, Job);
        Assert.Equal(
            (usages.Where(usage => usage.Job == Job).Sum(usage => (decimal)usage.InputTokens), usages.Where(usage => usage.Job == Job).Sum(usage => usage.Cost!.Amount), 13),
            (job.Tokens.Input, job.Costs.Single().Amount, repositories.UnattributedJobs));
    }

    [Fact]
    public async Task BatchesOfTwoInstancesDoNotWaitForEachOtherAsync()
    {
        await database.BeginAsync();
        Assert.NotEqual(ActivityLocks.Of(Desktop), ActivityLocks.Of(Laptop));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await using var holding = await Fleet.HoldLockAsync(connection, Desktop);

        var receipt = await BoundedAsync(Task.Run(() => fleet.IngestAsync(Laptop, "laptop", Usage(Guid.NewGuid(), Job, 5)), Cancellation));

        Assert.Equal((1, 0L), (receipt.Accepted.Count, await fleet.UngrantedLocksAsync(Laptop)));
    }
}
