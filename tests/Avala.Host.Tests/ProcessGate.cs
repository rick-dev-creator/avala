using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class ProcessGate : IPlugin
{
    public PluginInfo Info { get; } = new("process-gate", "Process gate");

    public ProcessesInFlight InFlight { get; } = new();

    public void Register(IPluginRegistrar registrar)
    {
        var real = registrar.Services.Last(service => service.ServiceType == typeof(IProcessRunner));
        registrar.Services.Remove(real);
        registrar.Services.AddSingleton(provider => InFlight.Counting(Real(provider, real)));
    }

    private static IProcessRunner Real(IServiceProvider provider, ServiceDescriptor real) =>
        real.ImplementationInstance as IProcessRunner
        ?? real.ImplementationFactory?.Invoke(provider) as IProcessRunner
        ?? (IProcessRunner)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
}
