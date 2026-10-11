using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class LostTrips : IPlugin
{
    private int armed;

    public PluginInfo Info { get; } = new("lost-trips", "Lost trips");

    public void Register(IPluginRegistrar registrar)
    {
        foreach (var handler in registrar.Services.Where(descriptor => descriptor.ServiceType == typeof(IHandle<BreakerTripped>)).ToList())
        {
            registrar.Services.Remove(handler);
            registrar.Services.AddSingleton<IHandle<BreakerTripped>>(services => new Lost((IHandle<BreakerTripped>)Resolve(services, handler), this));
        }
    }

    public void Arm() => Volatile.Write(ref armed, 1);

    private bool Armed => Volatile.Read(ref armed) == 1;

    private static object Resolve(IServiceProvider services, ServiceDescriptor handler) =>
        handler.ImplementationInstance
            ?? handler.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, handler.ImplementationType!);

    private sealed class Lost(IHandle<BreakerTripped> inner, LostTrips trips) : IHandle<BreakerTripped>
    {
        public ValueTask HandleAsync(BreakerTripped integrationEvent, CancellationToken cancellationToken) =>
            trips.Armed ? ValueTask.CompletedTask : inner.HandleAsync(integrationEvent, cancellationToken);
    }
}
