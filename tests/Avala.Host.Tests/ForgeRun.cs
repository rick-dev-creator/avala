using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;

namespace Avala.Host.Tests;

internal sealed class ForgeRun : IAsyncDisposable
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(1234);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private EventWatch<PullRequestWatchChanged> changes;
    private EventWatch<PullRequestWakeUp> wakeUps;
    private EventWatch<PullRequestOpened> opened;
    private EventWatch<JobProgressed> progress;
    private EventWatch<JobHeld> holds;
    private Task? paced;

    private ForgeRun(SimulatedRun run, JobId job)
    {
        Run = run;
        Job = job;
        changes = run.Watch<PullRequestWatchChanged>();
        wakeUps = run.Watch<PullRequestWakeUp>();
        opened = run.Watch<PullRequestOpened>();
        progress = run.Watch<JobProgressed>();
        holds = run.Watch<JobHeld>();
    }

    public async Task<JobHold> HeldAsync() => (await holds.UntilAsync(found => found.Hold.Job == Job)).Hold;

    public SimulatedRun Run { get; }

    public JobId Job { get; }

    public static string Rules(string forge, string policy, int wakeUps, string redeliver = "automatic") =>
        $$"""{ "approval": "pull-request", "pullRequest": { "forge": "{{forge}}", "onPullRequest": "{{policy}}", "maxWakeUps": {{wakeUps}}, "redeliver": "{{redeliver}}" } }""";

    public static Task<ForgeRun> StartAsync(PublishedPlugins plugins, string policy, int wakeUps, string scenario) =>
        StartAsync(plugins, Rules("local", policy, wakeUps), scenario, "fix-after-feedback");

    public static Task<ForgeRun> StartAsync(PublishedPlugins plugins, string rules, string scenario) =>
        StartAsync(plugins, rules, scenario, "fix-after-feedback");

    public static async Task<ForgeRun> StartAsync(PublishedPlugins plugins, string rules, string scenario, string agent, params (string Path, string Content)[] committed)
    {
        var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/jobs.json", rules), (".avala/permissions.json", Autonomous), .. committed]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, $"[simulate: {agent}] [forge: {scenario}] Fix the calculator")));
        var forge = new ForgeRun(run, job);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        return forge;
    }

    public static async Task<(ForgeRun Forge, EventWatch<TAsked> Asked)> SupervisedAsync<TAsked>(PublishedPlugins plugins, string rules, string scenario, string agent)
        where TAsked : IIntegrationEvent
    {
        var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/jobs.json", rules)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var asked = run.Watch<TAsked>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, $"[simulate: {agent}] [forge: {scenario}] Fix the calculator") { Autonomy = Autonomy.Supervised }));

        return (new ForgeRun(run, job), asked);
    }

    public static async Task<ForgeRun> PacedAsync(PublishedPlugins plugins, string rules, string scenario)
    {
        var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/jobs.json", rules), (".avala/permissions.json", Autonomous)], pace: Pace);
        _ = await run.Repository.PublishAsync(Cancellation);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, $"[simulate: fix-after-feedback] [forge: {scenario}] Fix the calculator")));
        var forge = new ForgeRun(run, job);
        _ = await forge.PacedUntilAsync(forge.ProgressedAsync(JobStatus.AwaitingReview));

        return forge;
    }

    public async Task<T> PacedUntilAsync<T>(Task<T> until)
    {
        paced ??= Run.PacedAsync();

        while (await Task.WhenAny(paced, until) == paced)
        {
            paced = Run.PacedAsync();
            Run.Pace();
        }

        return await until;
    }

    public async Task<PullRequestOpened> ApprovedAsync()
    {
        var approval = Outcomes.Succeeds(await Run.Get<IJobs>().ApproveAsync(Job, Cancellation));
        Assert.Equal(PullRequestDelivery.Strategy, approval.Delivery.Strategy);
        var announced = await opened.UntilAsync(found => found.Job == Job);
        _ = await ArmedAsync();

        return announced;
    }

    public async Task<WakeUpRecord> PollUntilWokenAsync()
    {
        Run.AdvanceTo(Outcomes.Present((await LatestAsync()).NextPoll));

        return await WokenAsync();
    }

    public async Task<WakeUpRecord> WokenAsync() => (await wakeUps.UntilAsync(found => found.WakeUp.Job == Job)).WakeUp;

    public async Task<PullRequestOpened> RedeliveredAsync()
    {
        var announced = await opened.UntilAsync(found => found.Job == Job);
        _ = await ArmedAsync();

        return announced;
    }

    public async Task<PullRequestWatchState> PollUntilAsync(Func<PullRequestWatchState, bool> match)
    {
        Run.AdvanceTo(Outcomes.Present((await LatestAsync()).NextPoll));

        return (await changes.UntilAsync(found => found.State.Job == Job && match(found.State))).State;
    }

    public async Task<PullRequestWatchState> PolledAgainAsync()
    {
        var due = Outcomes.Present((await LatestAsync()).NextPoll);
        Run.AdvanceTo(due);

        return (await changes.UntilAsync(found => found.State.Job == Job && found.State.LastPolled.Match(polled => polled >= due, () => false))).State;
    }

    public async Task<JobStatus> ProgressedAsync(JobStatus status) =>
        (await progress.UntilAsync(update => update.Job == Job && update.Status == status)).Status;

    public async Task<PullRequestWatchState> ChangedAsync(Func<PullRequestWatchState, bool> match) =>
        (await changes.UntilAsync(found => found.State.Job == Job && match(found.State))).State;

    public async Task<JobStatus> StatusAsync() =>
        Outcomes.Present(await Run.Get<IJobCatalog>().HistoryAsync(Job, Cancellation)).Summary.Status;

    public Task<string> RemoteStateAsync() => File.ReadAllTextAsync(Path.Combine(Run.Repository.Remote, "avala-forge.json"), Cancellation);

    public Task RestartAsync() =>
        Run.RestartAsync(() =>
        {
            changes = Run.Watch<PullRequestWatchChanged>();
            wakeUps = Run.Watch<PullRequestWakeUp>();
            opened = Run.Watch<PullRequestOpened>();
            progress = Run.Watch<JobProgressed>();
            holds = Run.Watch<JobHeld>();
        });

    public ValueTask DisposeAsync() => Run.DisposeAsync();

    private Task<PullRequestWatchState> LatestAsync() => Task.FromResult(Outcomes.Present(Run.Get<IPullRequests>().Of(Job)));

    private async Task<PullRequestWatchState> ArmedAsync() =>
        (await changes.UntilAsync(found => found.State.Job == Job && found.State.Status == WatchStatus.Watching && found.State.NextPoll.IsSome && found.State.Failures == 0)).State;
}
