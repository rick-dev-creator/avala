using Avala.Components.UI;
using Avala.Runtime;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.UI;
using Avala.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal sealed class CompositionRoot : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();

    private CompositionRoot(ServiceProvider services, ViewRegistry views)
    {
        Services = services;
        Views = views;
    }

    public ServiceProvider Services { get; }

    public ViewRegistry Views { get; }

    public Task Running { get; private set; } = Task.CompletedTask;

    public CancellationToken Lifetime => lifetime.Token;

    public static CompositionRoot Create(string pluginDirectory, AvalaPaths paths) =>
        Create(pluginDirectory, paths, new AvaloniaUiDispatcher());

    public static CompositionRoot Create(string pluginDirectory, AvalaPaths paths, LogFile log) =>
        Create(pluginDirectory, paths, new Surroundings(new AvaloniaUiDispatcher(), new AvaloniaFileOpener(), TimeProvider.System, []) { Log = log });

    public static CompositionRoot Create(string pluginDirectory, AvalaPaths paths, IUiDispatcher dispatcher) =>
        Create(pluginDirectory, paths, dispatcher, new AvaloniaFileOpener());

    public static CompositionRoot Create(string pluginDirectory, AvalaPaths paths, IUiDispatcher dispatcher, IFileOpener opener) =>
        Create(pluginDirectory, paths, new Surroundings(dispatcher, opener, TimeProvider.System, []));

    public static CompositionRoot Create(string pluginDirectory, AvalaPaths paths, IUiDispatcher dispatcher, TimeProvider clock, IReadOnlyList<IPlugin> replacements) =>
        Create(pluginDirectory, paths, dispatcher, clock, replacements, []);

    public static CompositionRoot Create(
        string pluginDirectory,
        AvalaPaths paths,
        IUiDispatcher dispatcher,
        TimeProvider clock,
        IReadOnlyList<IPlugin> replacements,
        IReadOnlyList<Type> left) =>
        Create(pluginDirectory, paths, dispatcher, clock, replacements, left, new AvaloniaLinkOpener());

    public static CompositionRoot Create(
        string pluginDirectory,
        AvalaPaths paths,
        IUiDispatcher dispatcher,
        TimeProvider clock,
        IReadOnlyList<IPlugin> replacements,
        IReadOnlyList<Type> left,
        ILinkOpener links) =>
        Create(pluginDirectory, paths, new Surroundings(dispatcher, new AvaloniaFileOpener(), clock, replacements) { Left = left, Links = links });

    private static CompositionRoot Create(string pluginDirectory, AvalaPaths paths, Surroundings surroundings)
    {
        var services = new ServiceCollection().AddLogging();
        _ = surroundings.Log.Match(log => services.AddSingleton(log), () => services);
        services
            .AddSingleton(surroundings.Clock)
            .AddRuntime(paths)
            .AddShell()
            .AddSingleton(surroundings.Dispatcher)
            .AddSingleton(surroundings.Opener)
            .AddSingleton(surroundings.Links);
        var views = new ViewRegistry();
        views.AddComponentViews();
        var registrar = new PluginRegistrar(services);

        foreach (var plugin in PluginLoader.Load(pluginDirectory).Where(surroundings.Kept).Select(surroundings.Replaced))
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

        try
        {
            await Running;
        }
        finally
        {
            await Services.DisposeAsync();
            lifetime.Dispose();
        }
    }

    private sealed record Surroundings(IUiDispatcher Dispatcher, IFileOpener Opener, TimeProvider Clock, IReadOnlyList<IPlugin> Replacements)
    {
        public IReadOnlyList<Type> Left { get; init; } = [];

        public ILinkOpener Links { get; init; } = new AvaloniaLinkOpener();

        public Option<LogFile> Log { get; init; }

        public bool Kept(IPlugin loaded) => !Left.Contains(loaded.GetType());

        public IPlugin Replaced(IPlugin loaded) => Replacements.FirstOrDefault(replacement => replacement.GetType() == loaded.GetType()) ?? loaded;
    }
}
