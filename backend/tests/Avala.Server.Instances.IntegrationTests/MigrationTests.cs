using Avala.Server.Instances.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class MigrationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Noon = PostgresFixture.Noon;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Insert = "INSERT INTO instances.instances (id, name, key_hash, registered_at) VALUES ({0}, {1}, {2}, {3})";

    [Fact]
    public async Task TheMigrationsCreateTheInstancesTableAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'instances' AND table_name = 'instances' ORDER BY column_name")
            .ToListAsync(Cancellation);
        var history = await context.Database
            .SqlQueryRaw<string>("SELECT to_regclass('instances.__migrations')::text AS \"Value\"")
            .SingleAsync(Cancellation);

        Assert.Equal(
            ("id, key_hash, name, registered_at, revoked_at", "instances.__migrations"),
            (string.Join(", ", columns), history));
    }

    [Fact]
    public async Task TwoInstancesCannotShareAKeyHashAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();
        var hash = InstanceKey.Hash(InstanceKey.Issue());

        await context.Database.ExecuteSqlRawAsync(Insert, [Guid.NewGuid(), "desktop", hash, Noon], Cancellation);
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(Insert, [Guid.NewGuid(), "laptop", hash, Noon], Cancellation));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
    }
}
