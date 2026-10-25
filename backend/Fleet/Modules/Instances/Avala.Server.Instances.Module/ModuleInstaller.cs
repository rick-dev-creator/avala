using System.Reflection;
using Avala.Server.CrossCuttingConcerns.Modules;
using Avala.Server.Instances.Application;
using Avala.Server.Instances.EndPoints;
using Avala.Server.Instances.EndPoints.Authentication;
using Avala.Server.Instances.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avala.Server.Instances.Module;

public sealed class ModuleInstaller : IModuleInstaller
{
    public string Name => "Instances";

    public Assembly Endpoints => EndpointCatalog.Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdministratorSettings>().BindConfiguration(AdministratorSettings.Section).ValidateOnStart();
        services.AddOptions<DatabaseSettings>().BindConfiguration(DatabaseSettings.Section);
        services.AddInstancesApplication()
            .AddInstancesInfrastructure(configuration)
            .AddInstancesAuthentication();
    }

    public Task UseAsync(IHost host, CancellationToken cancellationToken) =>
        host.Services.MigrateInstancesAsync(cancellationToken);
}
