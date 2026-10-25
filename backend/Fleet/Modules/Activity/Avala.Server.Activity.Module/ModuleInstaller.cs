using System.Reflection;
using Avala.Server.Activity.Application;
using Avala.Server.Activity.EndPoints;
using Avala.Server.Activity.Infrastructure;
using Avala.Server.CrossCuttingConcerns.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avala.Server.Activity.Module;

public sealed class ModuleInstaller : IModuleInstaller
{
    public string Name => "Activity";

    public Assembly Endpoints => EndpointCatalog.Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseSettings>().BindConfiguration(DatabaseSettings.Section);
        services.AddActivityApplication()
            .AddActivityInfrastructure(configuration);
    }

    public Task UseAsync(IHost host, CancellationToken cancellationToken) =>
        host.Services.MigrateActivityAsync(cancellationToken);
}
