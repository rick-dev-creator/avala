using System.Reflection;
using Avala.Server.CrossCuttingConcerns.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avala.Server.Violating.Module;

public sealed class ModuleInstaller : IModuleInstaller
{
    public string Name => "Violating";

    public Assembly Endpoints => typeof(ModuleInstaller).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
    }

    public Task UseAsync(IHost host, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ViolatingSettings
{
    public string Region { get; init; } = string.Empty;
}
