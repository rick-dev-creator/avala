using Avala.Sdk.UI;
using Avala.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal sealed class CompositionRoot(ServiceProvider services, ViewRegistry views)
{
    public ServiceProvider Services { get; } = services;

    public ViewRegistry Views { get; } = views;

    public static CompositionRoot Create(string pluginDirectory)
    {
        var services = new ServiceCollection().AddShell();
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
}
