using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ForgeTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string FailingTest = "1 test failed: CalculatorTests.AddsTwoNumbers";

    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailingCiWakesTheAgentWhoseFixIsPushedToTheSamePullRequestUntilItsChecksPassAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "ci-fails-once");

        var opened = await forge.ApprovedAsync();
        var wakeUp = await forge.PollUntilWokenAsync();
        var redelivered = await forge.RedeliveredAsync();
        var ended = await forge.PollUntilAsync(state => state.Status == WatchStatus.Ended);

        Assert.Equal((WakeReason.ChecksFailed, WakeOutcome.Woken, Option<ContinuedIn>.Some(ContinuedIn.ResumedConversation)), (wakeUp.Reason, wakeUp.Outcome, wakeUp.Conversation));
        Assert.Contains($"- build: {FailingTest}", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(opened.PullRequest.Number, redelivered.PullRequest.Number);
        Assert.Equal((Option<WatchEnd>.Some(WatchEnd.Green), 1), (ended.Ended, ended.WakeUps));
        Assert.Equal(JobStatus.Approved, await forge.StatusAsync());
        var history = Outcomes.Present(await forge.Run.Get<IJobCatalog>().HistoryAsync(forge.Job, Cancellation));
        Assert.Equal([AttemptOrigin.Initial, AttemptOrigin.SendBack], history.Attempts.Select(attempt => attempt.Origin));
        Assert.Equal(Option<string>.Some(wakeUp.Feedback), history.Attempts[1].Guidance);
        Assert.Equal([wakeUp], forge.Run.Get<IPullRequests>().WakeUpsOf(forge.Job));
    }

    [Fact]
    public async Task AReviewRequestingChangesWakesTheAgentWithItsCommentsWhenThePolicyWatchesReviewsAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci-and-reviews", 3, "changes-requested-once");

        _ = await forge.ApprovedAsync();
        var wakeUp = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var ended = await forge.PollUntilAsync(state => state.Status == WatchStatus.Ended);

        Assert.Equal(WakeReason.ChangesRequested, wakeUp.Reason);
        Assert.Contains("reviewer: Please greet the team by name.\n- GREETING.md:1: Name the team here.", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(Option<WatchEnd>.Some(WatchEnd.Green), ended.Ended);
    }

    [Fact]
    public async Task AConflictFetchesTheBaseAndWakesTheAgentToMergeItAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "conflict-once");

        _ = await forge.ApprovedAsync();
        var wakeUp = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var ended = await forge.PollUntilAsync(state => state.Status == WatchStatus.Ended);

        Assert.Equal(WakeReason.Conflict, wakeUp.Reason);
        Assert.Contains("Avala fetched origin/main: merge it into your branch and resolve the conflicts.", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(Option<WatchEnd>.Some(WatchEnd.Green), ended.Ended);
    }

    [Fact]
    public async Task WhenTheWakeUpsAreSpentTheWatchHoldsThePullRequestForAPersonAndSaysSoOnItAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 1, "ci-always-fails");

        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var held = await forge.PollUntilAsync(state => state.Status == WatchStatus.NeedsPerson);

        Assert.Equal((1, Option<WakeReason>.Some(WakeReason.ChecksFailed)), (held.WakeUps, held.Pending));
        Assert.Equal([WakeOutcome.Woken, WakeOutcome.HeldForPerson], forge.Run.Get<IPullRequests>().WakeUpsOf(forge.Job).Select(wakeUp => wakeUp.Outcome));
        Assert.Equal(JobStatus.Approved, await forge.StatusAsync());
        Assert.Contains("Avala stopped after 1 wake-up of the agent: its checks still fail. A person needs to look.", await forge.RemoteStateAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderReviewRedeliveryAWokenFixThatPassesItsGatesWaitsForAPersonWhoseApprovalPushesItToTheSamePullRequestAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, ForgeRun.Rules("local", "wake-on-ci", 3, "review"), "ci-fails-once");

        var opened = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.ProgressedAsync(JobStatus.Approved);
        _ = await forge.ProgressedAsync(JobStatus.Running);
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        var approval = Outcomes.Succeeds(await forge.Run.Get<IOpenDeliveries>().ApproveThroughAsync(forge.Job, PullRequestDelivery.Strategy, Cancellation));
        var redelivered = await forge.RedeliveredAsync();
        var ended = await forge.PollUntilAsync(state => state.Status == WatchStatus.Ended);

        Assert.Equal((PullRequestDelivery.Strategy, opened.PullRequest.Number), (approval.Delivery.Strategy, redelivered.PullRequest.Number));
        Assert.Equal((Redelivery.Review, Option<WatchEnd>.Some(WatchEnd.Green)), (ended.Redelivery, ended.Ended));
    }

    [Fact]
    public async Task AWokenRoundIsChargedToTheJobsBudgetAndAJobHeldAtItsCapIsNeverWokenAgainAsync()
    {
        await using var forge = await ForgeRun.StartAsync(
            plugins,
            ForgeRun.Rules("local", "wake-on-ci", 3),
            "ci-always-fails",
            "spend-on-fix",
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.02 } }"""));

        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        var hold = await forge.HeldAsync();
        var polled = await forge.PolledAgainAsync();

        Assert.Equal(HoldReason.BudgetExceeded, hold.Reason);
        Assert.Equal((WatchStatus.WaitingForJob, 1), (polled.Status, polled.WakeUps));
        Assert.Equal(JobStatus.NeedsHelp, await forge.StatusAsync());
        Assert.Single(forge.Run.Get<IPullRequests>().WakeUpsOf(forge.Job));
    }

    [Fact]
    public async Task ARestartWhileWatchingRestoresTheWatchAndItsNextPollWakesTheAgentAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "ci-fails-once");
        var opened = await forge.ApprovedAsync();

        await forge.RestartAsync();
        var wakeUp = await forge.WokenAsync();

        Assert.Equal((opened.PullRequest.Number, WakeReason.ChecksFailed, WakeOutcome.Woken), (wakeUp.PullRequest, wakeUp.Reason, wakeUp.Outcome));
        Assert.Equal(WatchStatus.WaitingForJob, (await forge.ChangedAsync(state => state.WakeUps == 1)).Status);
    }

    [Fact]
    public async Task ARepositoryThatNamesAnUnknownForgeLeavesTheJobAwaitingReviewAndSaysWhyAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/jobs.json", ForgeRun.Rules("elsewhere", "wake-on-ci", 3)), (".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var refused = run.Watch<PullRequestNotOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);

        Assert.Equal(JobRejection.DeliveryFailed, Outcomes.FailsWith(await run.Get<IJobs>().ApproveAsync(job, Cancellation)));

        Assert.Equal(new PullRequestNotOpened(job, ForgeError.UnknownConnection), await refused.UntilAsync(_ => true));
        Assert.Equal(JobStatus.AwaitingReview, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status);
    }

    private sealed class ForgeRun : IAsyncDisposable
    {
        private EventWatch<PullRequestWatchChanged> changes;
        private EventWatch<PullRequestWakeUp> wakeUps;
        private EventWatch<PullRequestOpened> opened;
        private EventWatch<JobProgressed> progress;
        private EventWatch<JobHeld> holds;

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
}
