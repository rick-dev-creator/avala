using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using Avala.Host.Composition;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Shell;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avala.Host.Tests;

internal sealed class DogfoodApp(PublishedPlugins plugins, DogfoodPlace place, DogfoodJournal journal, DogfoodCamera camera) : IAsyncDisposable
{
    private CancellationTokenSource subscriptions = new();
    private LogFile? log;
    private CompositionRoot? root;
    private ShellViewModel? shell;
    private Window? window;

    public Stopwatch Clock { get; } = Stopwatch.StartNew();

    public DogfoodCamera Camera => camera;

    public Channel<IIntegrationEvent> Events { get; } = Channel.CreateUnbounded<IIntegrationEvent>();

    public DogfoodUi Ui { get; private set; } = null!;

    public ShellViewModel Shell => shell!;

    public Window Window => window!;

    public bool Running => root is not null;

    public T Get<T>()
        where T : notnull => root!.Services.GetRequiredService<T>();

    public async Task ComposeAsync()
    {
        var paths = new AvalaPaths(place.Data);
        log = new LogFile(paths, TimeProvider.System);
        root = CompositionRoot.Create(plugins.Directory, paths, log, Option<Func<HttpMessageHandler>>.None);
        subscriptions = new CancellationTokenSource();
        Subscribe(root);
        root.Start();
        var logger = root.Services.GetRequiredService<ILogger<DogfoodApp>>();
        var data = place.Data;
        DogfoodLog.Composed(logger, data);
        shell = root.Services.GetRequiredService<ShellViewModel>();
        window = new ShellView { Width = 1440, Height = 900 };
        window.DataTemplates.Add(root.Views);
        window.DataContext = shell;
        Avala.Components.UI.Theme.Motion.SetIsReduced(window, true);
        shell.Activate();
        window.Show();
        camera.Window = window;
        Ui = new DogfoodUi(journal, camera) { Root = root };
        await journal.NoteAsync($"Composed the real application from {plugins.Directory}, data folder {place.Data}, repository {place.Repository}");
    }

    public async Task StopAsync()
    {
        if (root is null)
        {
            return;
        }

        var logger = root.Services.GetRequiredService<ILogger<DogfoodApp>>();
        DogfoodLog.Stops(logger);
        shell!.Deactivate();
        window!.Close();
        await subscriptions.CancelAsync();
        await root.DisposeAsync();
        await log!.DisposeAsync();
        subscriptions.Dispose();
        root = null;
        await journal.NoteAsync("Stopped the application");
    }

    public async Task RestartAsync()
    {
        await journal.NoteAsync("RESTART: stopping Avala in process");
        await StopAsync();
        await ComposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Events.Writer.TryComplete();
    }

    private void Subscribe(CompositionRoot composed)
    {
        var feed = composed.Services.GetRequiredService<IEventFeed>();
        var types = DogfoodLeftovers.EventTypes();
        var listen = typeof(DogfoodApp).GetMethod(nameof(ListenAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var token = subscriptions.Token;

        foreach (var type in types)
        {
            _ = (Task)listen.MakeGenericMethod(type).Invoke(this, [feed, token])!;
        }
    }

    private async Task ListenAsync<TEvent>(IEventFeed feed, CancellationToken token)
        where TEvent : IIntegrationEvent
    {
        try
        {
            await foreach (var happened in feed.SubscribeAsync<TEvent>(token))
            {
                await journal.EventAsync(Clock.Elapsed, happened);
                Events.Writer.TryWrite(happened);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal static partial class DogfoodLog
{
    public const string Stopping = "The dogfood driver stops Avala";

    [LoggerMessage(Level = LogLevel.Information, Message = "The dogfood driver composed Avala on the data folder {Data}")]
    public static partial void Composed(ILogger logger, string data);

    [LoggerMessage(Level = LogLevel.Information, Message = Stopping)]
    public static partial void Stops(ILogger logger);
}
