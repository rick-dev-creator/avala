using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class SimulatedRun : IAsyncDisposable
{
    private static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Failed];

    private readonly CancellationTokenSource subscriptions = new();
    private readonly TemporaryFolder data;
    private readonly CompositionRoot root;
    private readonly TemporaryRepository repository;
    private readonly EventWatch<JobProgressed> progress;
    private readonly EventWatch<AgentActivity> activity;
    private readonly EventWatch<CanvasUpdated> canvases;
    private readonly EventWatch<PermissionDecided> decisions;

    private SimulatedRun(TemporaryFolder data, CompositionRoot root, TemporaryRepository repository)
    {
        this.data = data;
        this.root = root;
        this.repository = repository;
        progress = Watch<JobProgressed>();
        activity = Watch<AgentActivity>();
        canvases = Watch<CanvasUpdated>();
        decisions = Watch<PermissionDecided>();
    }

    public TemporaryRepository Repository => repository;

    public ICanvases Canvases => root.Services.GetRequiredService<ICanvases>();
    public JobId Job { get; private set; }

    public string Worktree => Assert.Single(Directory.GetDirectories(new AvalaPaths(data.Path).Folder("worktrees")));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static Task<SimulatedRun> StartAsync(PublishedPlugins plugins, string scenario, params (string Path, string Content)[] committed) =>
        StartAsync(plugins, scenario, [], committed);

    public static Task<SimulatedRun> SupervisedAsync(PublishedPlugins plugins, string scenario, TimeSpan silence) =>
        StartAsync(plugins, scenario, [("supervision.json", $$"""{ "silenceSeconds": {{silence.TotalSeconds.ToString(CultureInfo.InvariantCulture)}} }""")], []);

    private static async Task<SimulatedRun> StartAsync(
        PublishedPlugins plugins,
        string scenario,
        IReadOnlyList<(string File, string Content)> settings,
        IReadOnlyList<(string Path, string Content)> committed)
    {
        var data = new TemporaryFolder();

        foreach (var (file, content) in settings)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, file), content, Cancellation);
        }

        var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var repository = await TemporaryRepository.CreateAsync(root.Services.GetRequiredService<IProcessRunner>(), Cancellation);

        foreach (var (path, content) in committed)
        {
            await repository.CommitAsync(path, content, Cancellation);
        }

        var run = new SimulatedRun(data, root, repository);
        root.Start();
        await run.SubmitAsync(scenario);

        return run;
    }

    public T Get<T>()
        where T : notnull =>
        root.Services.GetRequiredService<T>();

    public async Task<JobStatus> SettledAsync() =>
        (await progress.UntilAsync(update => Settled.Contains(update.Status))).Status;

    public async Task<IReadOnlyList<JobStatus>> JourneyAsync() =>
        [.. (await progress.CollectUntilAsync(update => Settled.Contains(update.Status))).Select(update => update.Status)];

    public async Task<IReadOnlyList<IAgentEvent>> TurnAsync() =>
        [.. (await activity.CollectUntilAsync(update => update.Event is TurnCompleted)).Select(update => update.Event)];

    public async Task<PolicyDecision> DecisionAsync() => (await decisions.UntilAsync(_ => true)).Decision;

    public async Task<IReadOnlyList<CanvasSnapshot>> CanvasSnapshotsAsync(int canvasCount)
    {
        var closed = 0;

        return
        [
            .. (await canvases.CollectUntilAsync(update => update.Snapshot.Status != CanvasStatus.Streaming && ++closed == canvasCount))
                .Select(update => update.Snapshot),
        ];
    }

    public async ValueTask DisposeAsync()
    {
        await subscriptions.CancelAsync();
        await root.DisposeAsync();
        await repository.DisposeAsync();
        data.Dispose();
        subscriptions.Dispose();
    }

    private async Task SubmitAsync(string scenario) =>
        Job = Outcomes.Succeeds(await Get<IJobs>()
            .SubmitAsync(new JobRequest(repository.Path, $"[simulate: {scenario}] Greet the team"), Cancellation));

    private EventWatch<TEvent> Watch<TEvent>()
        where TEvent : IIntegrationEvent =>
        new(root.Services.GetRequiredService<IEventFeed>().SubscribeAsync<TEvent>(subscriptions.Token), Cancellation);
}
