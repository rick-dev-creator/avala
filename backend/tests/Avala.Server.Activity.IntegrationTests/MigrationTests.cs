using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class MigrationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheMigrationsCreateTheActivityTablesAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();

        var tables = await context.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'activity' ORDER BY table_name")
            .ToListAsync(Cancellation);

        Assert.Equal("__migrations, instance_costs, instances, job_costs, jobs, quarter_costs, quarter_usage, received_events", string.Join(", ", tables));
    }
}
