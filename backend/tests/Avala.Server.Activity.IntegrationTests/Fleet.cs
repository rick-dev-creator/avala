using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Ingestion;
using Avala.Server.Activity.Infrastructure.Repositories;
using Avala.Server.Activity.Infrastructure.RepositoryJobs;
using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using JobStatus = Avala.Server.Activity.Contracts.JobStatus;
using HoldReason = Avala.Server.Activity.Contracts.HoldReason;
using CostBasis = Avala.Server.Activity.Contracts.CostBasis;

namespace Avala.Server.Activity.IntegrationTests;

internal sealed class Fleet(PostgresFixture database)
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public const string AvalaRemote = "remote:github.com/rick/avala";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly FakeTimeProvider clock = new(Noon.AddHours(1));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static JobSubmittedEvent Submitted(Guid id, Guid job, string repository, DateTimeOffset at) =>
        new(id, at, job, repository, null, "opus", "high");

    public static JobProgressedEvent Progressed(Guid id, Guid job, JobStatus status, DateTimeOffset at) => new(id, at, job, status);

    public static JobHeldEvent Held(Guid id, Guid job, HoldReason reason, DateTimeOffset at) => new(id, at, job, reason);

    public static UsageReportedEvent Usage(Guid id, Guid? job, long input, decimal? cost = null, DateTimeOffset? at = null) =>
        new(id, at ?? Noon, job, input, 1, 0, 0, 0, cost is { } amount ? new IncomingCost(amount, "USD", CostBasis.ApiEquivalent) : null, null);

    public async Task<IngestionReceipt> IngestAsync(Guid instance, string name, params IIncomingEvent[] events)
    {
        await using var context = database.Context();

        return (await new IngestEventsHandler(context, clock, NullLogger<IngestEventsHandler>.Instance)
            .Handle(new IngestEvents(instance, name, events), Cancellation)).Value;
    }

    public async Task<FleetRepositories> RepositoriesAsync()
    {
        await using var context = database.Context();

        return (await new ListRepositoriesHandler(context).Handle(new ListRepositories(), Cancellation)).Value;
    }

    public async Task<IReadOnlyList<RepositoryJob>> JobsAsync(string key)
    {
        await using var context = database.Context();

        return (await new ListRepositoryJobsHandler(context).Handle(new ListRepositoryJobs(key), Cancellation)).Value;
    }

    public async Task<FleetJob> JobAsync(Guid instance, Guid job)
    {
        await using var context = database.Context();

        return await context.Jobs.AsNoTracking().SingleAsync(found => found.InstanceId == instance && found.JobId == job, Cancellation);
    }

    public static async Task<NpgsqlTransaction> HoldLockAsync(NpgsqlConnection connection, Guid instance)
    {
        await connection.OpenAsync(Cancellation);
        var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@ingestion, @instance)", connection, transaction);
        command.Parameters.AddWithValue("ingestion", ActivityLocks.Ingestion);
        command.Parameters.AddWithValue("instance", ActivityLocks.Of(instance));
        await command.ExecuteNonQueryAsync(Cancellation);

        return transaction;
    }

    public async Task<bool> WaitsForLockAsync(Guid instance)
    {
        var time = TimeProvider.System;
        var deadline = time.GetUtcNow() + Patience;

        while (time.GetUtcNow() < deadline)
        {
            if (await UngrantedLocksAsync(instance) > 0)
            {
                return true;
            }

            await TickAsync(time);
        }

        return false;
    }

    public async Task<long> UngrantedLocksAsync(Guid instance)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
              AND classid = @ingestion::oid AND objid = @instance::oid AND objsubid = 2
            """,
            connection);
        command.Parameters.AddWithValue("ingestion", ActivityLocks.Ingestion);
        command.Parameters.AddWithValue("instance", ActivityLocks.Of(instance));

        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    public static Task<T> BoundedAsync<T>(Task<T> task) => task.WaitAsync(Patience, Cancellation);

    private static async Task TickAsync(TimeProvider time)
    {
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var timer = time.CreateTimer(_ => tick.TrySetResult(), state: null, PollInterval, Timeout.InfiniteTimeSpan);
        await tick.Task.WaitAsync(Cancellation);
    }
}
