using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Navigation;
using Avala.Preview.Origins;
using Avala.Preview.Pane;
using Avala.Preview.Running;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Tests.Pane;

internal sealed class FakeWebSurface(SurfaceRules rules, SurfaceEmbedding embedding) : IWebSurface
{
    private readonly Stack<Uri> history = new();

    public SurfaceEmbedding Embedding { get; } = embedding;

    public Option<Uri> Address => history.Count > 0 ? history.Peek() : Option<Uri>.None;

    public bool CanGoBack => history.Count > 1;

    public List<Uri> Visited { get; } = [];

    public int Reloads { get; private set; }

    public bool IsClosed { get; private set; }

    public event EventHandler<Uri>? Refused;

    public event EventHandler? Changed;

    public void Navigate(Uri address) => Follow(address);

    public void Follow(Uri address)
    {
        if (!rules.Admits(address))
        {
            Refused?.Invoke(this, address);

            return;
        }

        history.Push(address);
        Visited.Add(address);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Loads(Uri resource) => rules.AdmitsResource(resource);

    public void Reload() => Reloads++;

    public void GoBack()
    {
        _ = history.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => IsClosed = true;
}

internal sealed class FakeWebSurfaces : IWebSurfaces
{
    public SurfaceEmbedding Embedding { get; set; } = SurfaceEmbedding.Embedded;

    public List<FakeWebSurface> Opened { get; } = [];

    public FakeWebSurface Last => Opened[^1];

    public IWebSurface Open(SurfaceRules rules)
    {
        var surface = new FakeWebSurface(rules, Embedding);
        Opened.Add(surface);

        return surface;
    }
}

internal sealed class FakePreviews(PreviewBook book) : IPreviews
{
    public ConcurrentDictionary<JobId, string> Scripts { get; } = new();

    public ConcurrentDictionary<JobId, bool> Running { get; } = new();

    public Option<PreviewError> Refusal { get; set; }

    public IReadOnlyList<PreviewTarget> Of(JobId job) => book.Of(job);

    public bool IsRunning(JobId job) => Running.GetValueOrDefault(job);

    public ValueTask<Option<string>> ScriptAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Scripts.TryGetValue(job, out var script) ? Option<string>.Some(script) : Option<string>.None);

    public ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Refusal.Match(
            Result<PreviewRun, PreviewError>.Failure,
            () =>
            {
                Running[job] = true;

                return Result<PreviewRun, PreviewError>.Success(new PreviewRun(job, Scripts[job]));
            }));

    public ValueTask<Result<PreviewRun, PreviewError>> StopAsync(JobId job, CancellationToken cancellationToken)
    {
        Running[job] = false;

        return ValueTask.FromResult(Result<PreviewRun, PreviewError>.Success(new PreviewRun(job, Scripts[job])));
    }
}

internal sealed class FakeEventFeed : IEventFeed
{
    private readonly ConcurrentDictionary<Type, ImmutableList<Channel<IIntegrationEvent>>> subscribers = new();

    public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var channel = Channel.CreateUnbounded<IIntegrationEvent>();
        subscribers.AddOrUpdate(typeof(TEvent), [channel], (_, known) => known.Add(channel));

        return channel.Reader.ReadAllAsync(cancellationToken).Select(each => (TEvent)each);
    }

    public Task DeliveredAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Publish<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        foreach (var channel in subscribers.GetValueOrDefault(typeof(TEvent), []))
        {
            _ = channel.Writer.TryWrite(integrationEvent);
        }
    }
}

internal sealed class FakeLinks : ILinkOpener
{
    public List<Uri> Opened { get; } = [];

    public ValueTask<Result<Uri, FileOpenError>> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        Opened.Add(link);

        return ValueTask.FromResult(Result<Uri, FileOpenError>.Success(link));
    }
}

internal sealed class FakeGateways : IGateways
{
    public ConcurrentDictionary<JobId, bool> Network { get; } = new();

    public ValueTask<Result<int, PreviewError>> ServeFilesAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<int, PreviewError>.Failure(PreviewError.NotStarted));

    public ValueTask StopFilesAsync(JobId job, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public const int Offset = 10_000;

    public ConcurrentDictionary<JobId, ImmutableHashSet<int>> Ports { get; } = new();

    public static Uri Through(PreviewTarget target) => new($"http://127.0.0.1:{target.Address.Port + Offset}/");

    public ValueTask<Result<Uri, PreviewError>> ProxyAsync(PreviewTarget target, CancellationToken cancellationToken)
    {
        Ports.AddOrUpdate(target.Job, [target.Address.Port + Offset], (_, known) => known.Add(target.Address.Port + Offset));

        return ValueTask.FromResult(Result<Uri, PreviewError>.Success(Through(target)));
    }

    public ValueTask CloseAsync(JobId job, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public IReadOnlySet<int> PortsOf(JobId job) => Ports.GetValueOrDefault(job, []);

    public ValueTask AllowNetworkAsync(JobId job, bool allowed, CancellationToken cancellationToken)
    {
        Network[job] = allowed;

        return ValueTask.CompletedTask;
    }
}

internal sealed class PaneBench : IDisposable
{
    public PaneBench()
    {
        Previews = new FakePreviews(Book);
    }

    public JobId Job { get; } = JobId.New();

    public JobId Other { get; } = JobId.New();

    public PreviewBook Book { get; } = new();

    public FakePreviews Previews { get; }

    public FakeEventFeed Feed { get; } = new();

    public TestUiDispatcher Ui { get; } = new();

    public FakeWebSurfaces Surfaces { get; } = new();

    public FakeLinks Links { get; } = new();

    public FakeGateways Pages { get; } = new();

    public IMessenger Messenger { get; } = new StrongReferenceMessenger();

    public static Uri Local(int port) => new($"http://localhost:{port}/");

    public PreviewDesk Desk() => new(Previews, Book, Pages, new PreviewFeed(Feed, Ui));

    public PreviewPaneViewModel Pane() => new(Desk(), Surfaces, Links, Messenger);

    public RunningChipViewModel Chip(ChipPlacement placement) => new(Desk(), Messenger, placement);

    public RunScriptViewModel Run() => new(Desk(), Messenger);

    public PreviewTarget Serve(JobId job, int port, bool leased = true)
    {
        var target = new PreviewTarget(job, Local(port), PreviewSource.Server) { Leased = leased };
        var lease = new PortBlock(24_000, 24_009);
        Book.Keep(Book.Sighted.SetItem(job, new Sighting("/w/job", lease, [port], [target])));
        Feed.Publish(new PreviewAvailable(target));

        return target;
    }

    public void Stop(PreviewTarget target)
    {
        Book.Keep(Book.Sighted.Remove(target.Job));
        Feed.Publish(new PreviewGone(target));
    }

    public void Dispose() => Ui.Dispose();
}
