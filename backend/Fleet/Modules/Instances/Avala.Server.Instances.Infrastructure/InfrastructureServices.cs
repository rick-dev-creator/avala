using Avala.Server.Instances.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Server.Instances.Infrastructure;

internal static class InfrastructureServices
{
    public const string Database = "fleet";

    public static IServiceCollection AddInstancesInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<InstancesDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString(Database),
            npgsql => npgsql.MigrationsHistoryTable("__migrations", InstancesDbContext.Schema)));
}
