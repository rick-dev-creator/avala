using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.ClaudeCode;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Supervision.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Sdk.UI;
using Avala.Simulator;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

internal sealed class SimulatedRun : IAsyncDisposable
{
    private static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Failed];

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly PublishedPlugins plugins;
    private readonly TemporaryFolder data;
    private readonly TemporaryRepository repository;
    private readonly Surroundings surroundings;
    private Application application;
    private bool stopped;

    private SimulatedRun(PublishedPlugins plugins, TemporaryFolder data, TemporaryRepository repository, CompositionRoot root, Surroundings surroundings)
    {
        this.plugins = plugins;
        this.data = data;
        this.repository = repository;
        this.surroundings = surroundings;
        application = new Application(root);
        Ui = surroundings.Ui;
        Clock = surroundings.Clock;
    }

    public TemporaryRepository Repository => repository;

    public TestUiDispatcher Ui { get; }

    public FakeTimeProvider Clock { get; }

    public RecordingLinks Links => surroundings.Links;

    public BoardGate Board => surroundings.Board;

    public ICanvases Canvases => Get<ICanvases>();

    public ViewRegistry Views => application.Root.Views;

    public JobId Job { get; private set; }

    public string Worktree => Assert.Single(Directory.GetDirectories(new AvalaPaths(data.Path).Folder("worktrees")));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static Task<SimulatedRun> StartAsync(PublishedPlugins plugins, string scenario, params (string Path, string Content)[] committed) =>
        StartAsync(plugins, Simulate(scenario), Option<Autonomy>.None, [], committed);

    public static Task<SimulatedRun> StartAsync(
        PublishedPlugins plugins,
        string scenario,
        Autonomy autonomy,
        params (string Path, string Content)[] committed) =>
        StartAsync(plugins, Simulate(scenario), autonomy, [], committed);

    private static Task<SimulatedRun> StartAsync(
        PublishedPlugins plugins,
        string instruction,
        Option<Autonomy> autonomy,
        IReadOnlyList<(string File, string Content)> settings,
        IReadOnlyList<(string Path, string Content)> committed) =>
        StartAsync(plugins, new JobRequest(string.Empty, instruction) { Autonomy = autonomy }, settings, committed);

    private static async Task<SimulatedRun> StartAsync(
        PublishedPlugins plugins,
        JobRequest request,
        IReadOnlyList<(string File, string Content)> settings,
        IReadOnlyList<(string Path, string Content)> committed,
        bool harnesses = false,
        Option<IPlugin> claudeCode = default)
    {
        var run = await PreparedAsync(plugins, settings, committed, harnesses, claudeCode: claudeCode);
        run.Job = Outcomes.Succeeds(await run.SubmitAsync(request));

        return run;
    }

    public static Task<SimulatedRun> SupervisedAsync(PublishedPlugins plugins, string scenario, TimeSpan silence) =>
        StartAsync(
            plugins,
            Simulate(scenario),
            Option<Autonomy>.None,
            [("supervision.json", $$"""{ "silenceSeconds": {{silence.TotalSeconds.ToString(CultureInfo.InvariantCulture)}} }""")],
            []);

    public static Task<SimulatedRun> InstructedAsync(
        PublishedPlugins plugins,
        string instruction,
        IReadOnlyList<(string File, string Content)> data,
        IReadOnlyList<(string Path, string Content)> committed) =>
        StartAsync(plugins, instruction, Option<Autonomy>.None, data, committed);

    public static Task<SimulatedRun> ConnectedAsync(
        PublishedPlugins plugins,
        string scenario,
        Option<ConnectionName> connection,
        IReadOnlyList<(string File, string Content)> data,
        params (string Path, string Content)[] committed) =>
        StartAsync(plugins, new JobRequest(string.Empty, Simulate(scenario)) { Connection = connection }, data, committed);

    public static string Simulate(string scenario) => $"[simulate: {scenario}] Greet the team";

    public static Task<SimulatedRun> RealAsync(
        PublishedPlugins plugins,
        string instruction,
        IReadOnlyList<(string File, string Content)> data,
        IReadOnlyList<(string Path, string Content)> committed) =>
        StartAsync(plugins, new JobRequest(string.Empty, instruction), data, committed, harnesses: true);

    public static Task<SimulatedRun> TranscribedAsync(
        PublishedPlugins plugins,
        IPlugin claudeCode,
        JobRequest request,
        IReadOnlyList<(string File, string Content)> data,
        IReadOnlyList<(string Path, string Content)> committed) =>
        StartAsync(plugins, request, data, committed, claudeCode: Option<IPlugin>.Some(claudeCode));

    public static Task<SimulatedRun> PreparedAsync(PublishedPlugins plugins, params (string Path, string Content)[] committed) =>
        PreparedAsync(plugins, [], committed);

    public static async Task<SimulatedRun> PreparedAsync(
        PublishedPlugins plugins,
        IReadOnlyList<(string File, string Content)> settings,
        IReadOnlyList<(string Path, string Content)> committed,
        bool harnesses = false,
        bool developer = true,
        Option<IPlugin> claudeCode = default)
    {
        var data = new TemporaryFolder();

        foreach (var (file, content) in settings)
        {
            var path = Path.Combine(data.Path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, Cancellation);
        }

        var surroundings = new Surroundings(new TestUiDispatcher(), new FakeTimeProvider(DateTimeOffset.UtcNow) { AutoAdvanceAmount = TimeSpan.FromTicks(1) }, harnesses, developer, claudeCode);
        var root = surroundings.Compose(plugins, data);
        var repository = await TemporaryRepository.CreateAsync(root.Services.GetRequiredService<IProcessRunner>(), Cancellation);

        foreach (var (path, content) in committed)
        {
            await repository.CommitAsync(path, content, Cancellation);
        }

        var run = new SimulatedRun(plugins, data, repository, root, surroundings);
        root.Start();

        return run;
    }

    public async Task DeliveredAsync() =>
        await application.Root.Services.GetRequiredService<IEventFeed>().DeliveredAsync(Cancellation).WaitAsync(HangGuard, Cancellation);

    public async Task QuietAsync()
    {
        _ = await application.Progress.UntilAsync(update => update.Job == Job && update.Status == JobStatus.Running);
        await ResumableAsync();
        await DeliveredAsync();
    }

    public async Task SilentForAsync(TimeSpan window)
    {
        await QuietAsync();
        Clock.Advance(window);
    }

    public void AdvanceTo(DateTimeOffset moment)
    {
        var now = Clock.GetUtcNow();
        Clock.Advance(moment > now ? moment - now : TimeSpan.Zero);
    }

    public EventWatch<TEvent> Watch<TEvent>()
        where TEvent : IIntegrationEvent =>
        application.Watch<TEvent>();

    public string DataFolder => data.Path;

    public async Task<Result<JobId, JobRejection>> SubmitAsync(JobRequest request) =>
        await Get<IJobs>().SubmitAsync(request with { RepositoryPath = repository.Path }, Cancellation);

    public async Task<IReadOnlyList<JobStatus>> SettledAsync(params JobId[] jobs)
    {
        var settled = new Dictionary<JobId, JobStatus>();
        _ = await application.Progress.UntilAsync(update =>
        {
            if (jobs.Contains(update.Job) && Settled.Contains(update.Status))
            {
                settled[update.Job] = update.Status;
            }

            return settled.Count == jobs.Length;
        });

        return [.. jobs.Select(job => settled[job])];
    }

    public async Task<JobStatus> SettledAsync() =>
        (await application.Progress.UntilAsync(update => Settled.Contains(update.Status))).Status;

    public async Task<SessionOpened> OpenedAsync() => await application.Opened.UntilAsync(_ => true);

    public async Task StartedAsync() => _ = await application.Started.UntilAsync(_ => true);

    public T Get<T>()
        where T : notnull =>
        application.Root.Services.GetRequiredService<T>();

    public async Task RestartAsync()
    {
        await application.DisposeAsync();
        application = new Application(surroundings.Compose(plugins, data));
        application.Root.Start();
    }

    public async Task<IReadOnlyList<JobStatus>> JourneyAsync() =>
        [.. (await application.Progress.CollectUntilAsync(update => Settled.Contains(update.Status))).Select(update => update.Status)];

    public async Task<IReadOnlyList<IAgentEvent>> TurnAsync() =>
        [.. (await application.Activity.CollectUntilAsync(update => update.Event is TurnCompleted)).Select(update => update.Event)];

    public async Task<PolicyDecision> DecisionAsync() => (await application.Decisions.UntilAsync(_ => true)).Decision;

    public async Task<IReadOnlyList<PolicyDecision>> DecisionsAsync(int count)
    {
        var decided = 0;

        return [.. (await application.Decisions.CollectUntilAsync(_ => ++decided == count)).Select(update => update.Decision)];
    }

    public async Task<FormDecision> FormDecisionAsync() => (await application.Forms.UntilAsync(_ => true)).Decision;

    public async Task<SessionAutonomy> AutonomyAsync() => (await application.Autonomies.UntilAsync(_ => true)).Autonomy;

    public async Task<JobHold> HoldAsync() => (await application.Holds.UntilAsync(_ => true)).Hold;

    public async Task<DelegationRecord> ChildReportedAsync() => (await application.Reported.UntilAsync(_ => true)).Delegation;

    public async Task<DelegationRecord> ReportDeliveredAsync() => (await application.Delivered.UntilAsync(_ => true)).Delegation;

    public async Task<string> ToldAsync() =>
        ((ItemProgressed)(await application.Activity.UntilAsync(update => update.Event is ItemProgressed { Item.Value: "told" } told && told.Text != Told)).Event).Text;

    private const string Told = "I was told: ";

    public async Task ResumableAsync() => _ = await application.Resumable.UntilAsync(update => update.Job == Job);

    public async Task<SupervisionIntervention> SupervisorInterventionAsync() => (await application.Supervision.UntilAsync(_ => true)).Intervention;

    public async Task<BudgetIntervention> BudgetInterventionAsync() => (await application.Budgets.UntilAsync(_ => true)).Intervention;

    public async Task UsageRecordedAsync(int reports)
    {
        var recorded = 0;
        _ = await application.Usage.UntilAsync(_ => ++recorded == reports);
    }

    public async Task<OrphanReport> OrphansFoundAsync() => (await application.Orphans.UntilAsync(_ => true)).Report;

    public async Task<ReclaimedWorktree> ReclaimedAsync() => (await application.Reclaimed.UntilAsync(_ => true)).Reclaimed;

    public async Task<PortLease> LeasedAsync() => (await application.Leased.UntilAsync(_ => true)).Lease;

    public async Task<JobQueued> QueuedAsync() => await application.Queued.UntilAsync(_ => true);

    public async Task<ResourceSample> SampledAsync(Func<ResourceSample, bool> match) =>
        (await application.Sampled.UntilAsync(sampled => match(sampled.Sample))).Sample;

    public async Task<IReadOnlyList<CanvasSnapshot>> CanvasSnapshotsAsync(int canvasCount)
    {
        var closed = 0;

        return
        [
            .. (await application.Canvases.CollectUntilAsync(update => update.Snapshot.Status != CanvasStatus.Streaming && ++closed == canvasCount))
                .Select(update => update.Snapshot),
        ];
    }

    public async Task<IReadOnlyList<string>> StopAndReadRecordingsAsync()
    {
        await StopAsync();
        var folder = new AvalaPaths(data.Path).Folder("recordings");

        return Directory.Exists(folder)
            ? await Task.WhenAll(Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).Select(file => File.ReadAllTextAsync(file, Cancellation)))
            : [];
    }

    public async Task<string> StopAndReadLogAsync()
    {
        await StopAsync();
        var folder = new AvalaPaths(data.Path).Logs;

        return string.Concat(await Task.WhenAll(Directory.GetFiles(folder).Order(StringComparer.Ordinal).Select(file => File.ReadAllTextAsync(file, Cancellation))));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await repository.DisposeAsync();
        await data.DisposeAsync();
        Ui.Dispose();
    }

    public Task<OpenWorkbench> WorkbenchAsync() => OpenWorkbench.ActivateAsync(this);

    public Bound Page(string title) => new(Assert.Single(Get<IEnumerable<IPage>>(), page => page.Title == title));

    private async Task StopAsync()
    {
        if (!stopped)
        {
            stopped = true;
            surroundings.Board.Release();
            await application.DisposeAsync();
        }
    }


    private sealed record Surroundings(TestUiDispatcher Ui, FakeTimeProvider Clock, bool Harnesses, bool Developer, Option<IPlugin> ClaudeCode)
    {
        public RecordingLinks Links { get; } = new();

        public BoardGate Board { get; } = new();

        public CompositionRoot Compose(PublishedPlugins plugins, TemporaryFolder data) =>
            CompositionRoot.Create(
                plugins.Directory,
                new AvalaPaths(data.Path),
                Ui,
                Clock,
                [new SimulatorPlugin(TimeSpan.Zero, Developer), .. ClaudeCode.Match<IPlugin[]>(plugin => [plugin], () => [])],
                Harnesses || ClaudeCode.IsSome ? [] : [typeof(ClaudeCodePlugin)],
                Links,
                [Board]);
    }

    private sealed class Application : IAsyncDisposable
    {
        private readonly CancellationTokenSource subscriptions = new();

        public Application(CompositionRoot root)
        {
            Root = root;
            Progress = Watch<JobProgressed>();
            Activity = Watch<AgentActivity>();
            Canvases = Watch<CanvasUpdated>();
            Decisions = Watch<PermissionDecided>();
            Holds = Watch<JobHeld>();
            Resumable = Watch<JobResumable>();
            Supervision = Watch<SupervisorIntervened>();
            Budgets = Watch<BudgetIntervened>();
            Usage = Watch<UsageRecorded>();
            Forms = Watch<FormDecided>();
            Autonomies = Watch<AutonomyApplied>();
            Opened = Watch<SessionOpened>();
            Orphans = Watch<OrphansFound>();
            Reclaimed = Watch<WorktreeReclaimed>();
            Leased = Watch<PortsLeased>();
            Queued = Watch<JobQueued>();
            Sampled = Watch<ResourcesSampled>();
            Started = Watch<StartupCompleted>();
            Reported = Watch<ChildReported>();
            Delivered = Watch<ReportDelivered>();
        }

        public EventWatch<StartupCompleted> Started { get; }

        public EventWatch<ChildReported> Reported { get; }

        public EventWatch<ReportDelivered> Delivered { get; }

        public EventWatch<OrphansFound> Orphans { get; }

        public EventWatch<WorktreeReclaimed> Reclaimed { get; }

        public EventWatch<PortsLeased> Leased { get; }

        public EventWatch<JobQueued> Queued { get; }

        public EventWatch<ResourcesSampled> Sampled { get; }

        public EventWatch<SessionOpened> Opened { get; }

        public EventWatch<FormDecided> Forms { get; }

        public EventWatch<AutonomyApplied> Autonomies { get; }

        public CompositionRoot Root { get; }

        public EventWatch<JobProgressed> Progress { get; }

        public EventWatch<AgentActivity> Activity { get; }

        public EventWatch<CanvasUpdated> Canvases { get; }

        public EventWatch<PermissionDecided> Decisions { get; }

        public EventWatch<JobHeld> Holds { get; }

        public EventWatch<JobResumable> Resumable { get; }

        public EventWatch<SupervisorIntervened> Supervision { get; }

        public EventWatch<BudgetIntervened> Budgets { get; }

        public EventWatch<UsageRecorded> Usage { get; }

        public async ValueTask DisposeAsync()
        {
            await subscriptions.CancelAsync();
            await Root.DisposeAsync();
            subscriptions.Dispose();
        }

        public EventWatch<TEvent> Watch<TEvent>()
            where TEvent : IIntegrationEvent =>
            new(Root.Services.GetRequiredService<IEventFeed>().SubscribeAsync<TEvent>(subscriptions.Token), Cancellation);
    }
}
