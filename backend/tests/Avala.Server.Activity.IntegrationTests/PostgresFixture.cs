using Avala.Server.Activity.Infrastructure;
using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.3";

    private PostgreSqlContainer? container;

    private ServiceProvider? services;

    private DbContextOptions<ActivityDbContext>? options;

    public bool Available { get; private set; }

    public string Reason { get; private set; } = "The container engine has not been probed yet.";

    public string ConnectionString => container!.GetConnectionString();

    internal ActivityDbContext Context() => new(options!);

    public async ValueTask InitializeAsync()
    {
        (Available, Reason) = await ContainerEngine.ProbeAsync();

        if (!Available)
        {
            return;
        }

        container = new PostgreSqlBuilder(Image).Build();
        await container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{InfrastructureServices.Database}", container.GetConnectionString())])
            .Build();
        services = new ServiceCollection()
            .AddOptions<DatabaseSettings>().Services
            .AddActivityInfrastructure(configuration)
            .BuildServiceProvider();
        await services.MigrateActivityAsync(CancellationToken.None);

        options = services.GetRequiredService<DbContextOptions<ActivityDbContext>>();
    }

    public async Task BeginAsync()
    {
        Assert.SkipUnless(Available, Reason);

        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync(
            $"TRUNCATE {ActivityDbContext.Schema}.job_costs, {ActivityDbContext.Schema}.jobs, {ActivityDbContext.Schema}.instance_costs, {ActivityDbContext.Schema}.instances, {ActivityDbContext.Schema}.received_events",
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
