using Avala.Notifications.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class NotifierGate : IPlugin, IDisposable
{
    public PluginInfo Info { get; } = new("notifier-gate", "Notifier gate");

    public FakeNotifierEndpoint Endpoint { get; } = new();

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services.AddKeyedSingleton<HttpMessageHandler>(NotificationTransport.Handler, Endpoint);

    public void Dispose() => Endpoint.Dispose();
}
