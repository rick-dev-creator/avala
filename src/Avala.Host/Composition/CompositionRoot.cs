using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.UI;
using Avala.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal sealed class CompositionRoot : IAsyncDisposable
{
    private static readonly string DataDirectory = Environment.GetEnvironmentVariable("AVALA_DATA_PATH") is { Length: > 0 } configured
        ? configured
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avala");

    private readonly CancellationTokenSource lifetime = new();

    private CompositionRoot(ServiceProvider services, ViewRegistry views)
    {
        Services = services;
        Views = views;
    }

    public ServiceProvider Services { get; }

    public ViewRegistry Views { get; }

    public Task Running { get; private set; } = Task.CompletedTask;

    public static CompositionRoot Create(string pluginDirectory)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddRuntime(new AvalaPaths(DataDirectory))
            .AddShell()
            .AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        var views = new ViewRegistry();
        var registrar = new PluginRegistrar(services);

        foreach (var plugin in PluginLoader.Load(pluginDirectory))
        {
            plugin.Register(registrar);

            if (plugin is IViewContributor contributor)
            {
                contributor.RegisterViews(views);
            }
        }

        return new CompositionRoot(services.BuildServiceProvider(), views);
    }

    public void Start() => Running = Services.RunAsync(lifetime.Token);

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Running;
        await Services.DisposeAsync();
        lifetime.Dispose();
    }
}
