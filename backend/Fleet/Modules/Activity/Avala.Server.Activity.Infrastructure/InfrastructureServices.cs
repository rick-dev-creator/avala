using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avala.Server.Activity.Infrastructure;

internal static class InfrastructureServices
{
    public const string Database = "fleet";

    public const string MigrationsHistory = "__migrations";

    public static IServiceCollection AddActivityInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<ActivityDbContext>(
            options => options.UseNpgsql(
                configuration.GetConnectionString(Database),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistory, ActivityDbContext.Schema)),
            ServiceLifetime.Scoped,
            ServiceLifetime.Singleton);

    public static async Task MigrateActivityAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!services.GetRequiredService<IOptions<DatabaseSettings>>().Value.ApplyMigrations)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ActivityDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
