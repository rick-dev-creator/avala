using Avala.Server.Instances.Infrastructure;
using Avala.Server.Instances.Infrastructure.Storage;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.3";

    private PostgreSqlContainer? container;

    private ServiceProvider? services;

    private DbContextOptions<InstancesDbContext>? options;

    public bool Available { get; private set; }

    public string Reason { get; private set; } = "The container engine has not been probed yet.";

    internal InstancesDbContext Context() => new(options!);

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
            .AddInstancesInfrastructure(configuration)
            .BuildServiceProvider();
        await services.MigrateInstancesAsync(CancellationToken.None);

        options = services.GetRequiredService<DbContextOptions<InstancesDbContext>>();
    }

    public static DateTimeOffset Noon { get; } = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public async Task BeginAsync()
    {
        Assert.SkipUnless(Available, Reason);

        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync($"TRUNCATE {InstancesDbContext.Schema}.instances", TestContext.Current.CancellationToken);
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
