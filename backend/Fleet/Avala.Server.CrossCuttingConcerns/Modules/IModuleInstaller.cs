using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avala.Server.CrossCuttingConcerns.Modules;

public interface IModuleInstaller
{
    string Name { get; }

    Assembly Endpoints { get; }

    void Install(IServiceCollection services, IConfiguration configuration);

    Task UseAsync(IHost host, CancellationToken cancellationToken);
}
