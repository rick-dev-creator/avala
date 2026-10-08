using Avala.Agents;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.UI;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Integration.Tests;

internal sealed class AvalaApplication : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly CancellationTokenSource lifetime = new();
    private Task running = Task.CompletedTask;

    private AvalaApplication(ServiceProvider services) => this.services = services;

    public static AvalaApplication Compose(string dataFolder, IAgentProvider provider)
    {
        var services = new ServiceCollection().AddLogging().AddRuntime(new AvalaPaths(dataFolder));
        var registrar = new PluginRegistrar(services);

        foreach (var plugin in (IPlugin[])[new AgentsPlugin(), new WorkspacesPlugin(), new JobsPlugin()])
        {
            plugin.Register(registrar);
        }

        services.AddSingleton(provider);

        return new AvalaApplication(services.BuildServiceProvider());
    }

    public T Get<T>()
        where T : notnull =>
        services.GetRequiredService<T>();

    public EventWatch<TEvent> Watch<TEvent>()
        where TEvent : IIntegrationEvent =>
        new(Get<IEventFeed>().SubscribeAsync<TEvent>(lifetime.Token), TestContext.Current.CancellationToken);

    public void Start() => running = services.RunAsync(lifetime.Token);

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await running;
        await services.DisposeAsync();
        lifetime.Dispose();
    }

    private sealed class PluginRegistrar(IServiceCollection services) : IPluginRegistrar
    {
        public IServiceCollection Services { get; } = services;
    }
}
