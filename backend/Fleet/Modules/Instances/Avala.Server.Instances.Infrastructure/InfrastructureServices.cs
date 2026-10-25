using Avala.Server.Instances.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.Infrastructure;

internal static class InfrastructureServices
{
    public const string Database = "fleet";

    public const string MigrationsHistory = "__migrations";

    public static IServiceCollection AddInstancesInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<InstancesDbContext>(
            options => options.UseNpgsql(
                configuration.GetConnectionString(Database),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistory, InstancesDbContext.Schema)),
            ServiceLifetime.Scoped,
            ServiceLifetime.Singleton);

    public static async Task MigrateInstancesAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!services.GetRequiredService<IOptions<DatabaseSettings>>().Value.ApplyMigrations)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InstancesDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
