using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

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
        var green = await forge.PollUntilAsync(state => state.Status == WatchStatus.Green);

        Assert.Equal((WakeReason.ChecksFailed, WakeOutcome.Woken, Option<ContinuedIn>.Some(ContinuedIn.ResumedConversation)), (wakeUp.Reason, wakeUp.Outcome, wakeUp.Conversation));
        Assert.Contains($"- build: {FailingTest}", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(opened.PullRequest.Number, redelivered.PullRequest.Number);
        Assert.Equal((Option<WatchEnd>.None, 1), (green.Ended, green.WakeUps));
        Assert.Equal(JobStatus.Approved, await forge.StatusAsync());
        var history = Outcomes.Present(await forge.Run.Get<IJobCatalog>().HistoryAsync(forge.Job, Cancellation));
        Assert.Equal([AttemptOrigin.Initial, AttemptOrigin.Reopen], history.Attempts.Select(attempt => attempt.Origin));
        Assert.Equal(Option<string>.Some(wakeUp.Feedback), history.Attempts[1].Guidance);
        Assert.Equal([wakeUp], forge.Run.Get<IPullRequests>().WakeUpsOf(forge.Job));
    }

    [Fact]
    public async Task APersonsMessageToAPullRequestJobTheWatchAlreadyWokeComesBackForReviewInsteadOfBeingPushedAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "ci-fails-once");
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var progressed = forge.Run.Watch<JobProgressed>();

        Outcomes.Succeeds(await forge.Run.Get<IJobs>().ContinueAsync(forge.Job, "Please also document the fix.", Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == forge.Job && update.Status == JobStatus.AwaitingReview);
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task AReviewRequestingChangesWakesTheAgentWithItsCommentsWhenThePolicyWatchesReviewsAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci-and-reviews", 3, "changes-requested-once");

        _ = await forge.ApprovedAsync();
        var wakeUp = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var green = await forge.PollUntilAsync(state => state.Status == WatchStatus.Green);

        Assert.Equal(WakeReason.ChangesRequested, wakeUp.Reason);
        Assert.Contains("reviewer: Please greet the team by name.\n- GREETING.md:1: Name the team here.", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(Option<WatchEnd>.None, green.Ended);
    }

    [Fact]
    public async Task AConflictFetchesTheBaseAndWakesTheAgentToMergeItAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "conflict-once");

        _ = await forge.ApprovedAsync();
        var wakeUp = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        var green = await forge.PollUntilAsync(state => state.Status == WatchStatus.Green);

        Assert.Equal(WakeReason.Conflict, wakeUp.Reason);
        Assert.Contains("Avala fetched origin/main: merge it into your branch and resolve the conflicts.", wakeUp.Feedback, StringComparison.Ordinal);
        Assert.Equal(Option<WatchEnd>.None, green.Ended);
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
        var green = await forge.PollUntilAsync(state => state.Status == WatchStatus.Green);

        Assert.Equal((PullRequestDelivery.Strategy, opened.PullRequest.Number), (approval.Delivery.Strategy, redelivered.PullRequest.Number));
        Assert.Equal((Redelivery.Review, Option<WatchEnd>.None), (green.Redelivery, green.Ended));
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
    public async Task AWokenFixWhoseFinalTurnReportsSpendOverTheCapIsHeldInsteadOfRedeliveredAsync()
    {
        await using var forge = await ForgeRun.StartAsync(
            plugins,
            ForgeRun.Rules("local", "wake-on-ci", 3),
            "ci-fails-once",
            "fix-after-feedback",
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.02 } }"""));

        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        var hold = await forge.HeldAsync();

        Assert.Equal(HoldReason.BudgetExceeded, hold.Reason);
        Assert.Equal(JobStatus.NeedsHelp, await forge.StatusAsync());
        var history = Outcomes.Present(await forge.Run.Get<IJobCatalog>().HistoryAsync(forge.Job, Cancellation));
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Passed), (AttemptOrigin.Reopen, AttemptOutcome.Interrupted)],
            history.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
    }

    [Fact]
    public async Task AWokenFixHeldAtItsUsageLimitAndResumedAtTheResetIsPushedToTheSamePullRequestAsync()
    {
        const string Rules = """{ "approval": "pull-request", "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 3 }, "limits": { "resumeAtReset": "auto" } }""";
        await using var forge = await ForgeRun.StartAsync(plugins, Rules, "ci-fails-once", "fix-past-the-window");
        var waits = forge.Run.Watch<JobWaitsForReset>();
        var opened = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        var wait = (await waits.UntilAsync(announced => announced.Wait.Job == forge.Job)).Wait;

        forge.Run.AdvanceTo(Outcomes.Present(wait.ResumesAt));
        var redelivered = await forge.RedeliveredAsync();

        Assert.Equal(opened.PullRequest.Number, redelivered.PullRequest.Number);
        Assert.Equal(JobStatus.Approved, await forge.StatusAsync());
    }

    [Fact]
    public async Task APersonsMessageToAWokenFixHeldAtItsUsageLimitComesBackForReviewInsteadOfBeingPushedAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, ForgeRun.Rules("local", "wake-on-ci", 3), "ci-fails-once", "fix-past-the-window");
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.HeldAsync();
        var progressed = forge.Run.Watch<JobProgressed>();

        Outcomes.Succeeds(await forge.Run.Get<IJobs>().ContinueAsync(forge.Job, "Stop: keep the old signature and only fix the sum.", Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == forge.Job && update.Status == JobStatus.AwaitingReview);
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task TheFailingChecksAWatchWakesADocumentedJobWithAreNoChangeRequestOfThePersonAsync()
    {
        const string Rules = """{ "approval": "pull-request", "docs": { "enabled": true }, "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 3 } }""";
        await using var forge = await ForgeRun.StartAsync(plugins, Rules, "ci-fails-once");
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();

        _ = await forge.RedeliveredAsync();
        await forge.Run.DeliveredAsync();

        Assert.Empty(forge.Run.Get<IDocumentation>().OfJob(forge.Job).Match(documented => documented.Requests, () => []));
    }

    [Fact]
    public async Task APersonsMessageThatJoinsAWokenFixWhileItRunsComesBackForReviewInsteadOfBeingPushedAsync()
    {
        await using var forge = await ForgeRun.PacedAsync(plugins, ForgeRun.Rules("local", "wake-on-ci", 3), "ci-fails-once");
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.PacedUntilAsync(forge.ProgressedAsync(JobStatus.Running));

        _ = Outcomes.Succeeds(await forge.Run.Get<IJobs>().SteerAsync(forge.Job, new JobMessage("Also rename add to sum everywhere."), Cancellation));
        _ = await forge.PacedUntilAsync(forge.ProgressedAsync(JobStatus.AwaitingReview));
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task ANoticeOfAPermissionThePersonAllowedAfterAWokenFixWasPushedComesBackForReviewInsteadOfBeingPushedAsync()
    {
        await using var forge = await ForgeRun.StartAsync(plugins, "wake-on-ci", 3, "ci-fails-once");
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();
        _ = await forge.RedeliveredAsync();
        _ = await forge.PollUntilAsync(state => state.Status == WatchStatus.Green);
        var progressed = forge.Run.Watch<JobProgressed>();

        Outcomes.Succeeds(await forge.Run.Get<IJobs>().ContinueAsync(forge.Job, new JobMessage("Avala now allows web requests to example.com in this job. You may retry it.") { Author = MessageAuthor.Avala }, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == forge.Job && update.Status == JobStatus.AwaitingReview);
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
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

    [Fact]
    public async Task APullRequestTheAgentOpenedFromItsJobIsWatchedOnceItsTurnEndsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation));

        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", "origin", worktree.Branch);
        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync(worktree.Branch, "main", "Edit the greeting", Cancellation);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Push your branch and open a pull request.", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((number, worktree.Branch), (adopted.PullRequest.Number, adopted.PullRequest.Head));
        var watch = Outcomes.Present(run.Get<IPullRequests>().Of(job));
        Assert.Equal((new ForgeName("local"), OnPullRequest.WatchOnly), (watch.Forge, watch.Policy));
    }

    [Fact]
    public async Task APullRequestTheAgentOpenedFromABranchItSwitchedToInItsWorktreeIsAdoptedAndWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (job, worktree) = await ReviewedEditAsync(run);

        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", "-b", "fix/greeting");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", "origin", "fix/greeting");
        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("fix/greeting", "main", "Edit the greeting", Cancellation);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Push your branch and open a pull request.", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((number, "fix/greeting"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
        Assert.True(run.Get<IPullRequests>().Of(job).IsSome);
    }

    [Fact]
    public async Task APullRequestTheAgentReportsFromABranchItNeverCheckedOutIsAdoptedAndWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (job, worktree) = await ReviewedEditAsync(run, "mentions");
        var number = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {PullRequestLink(number)}", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((number, "fix/rounding"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
        Assert.True(run.Get<IPullRequests>().Of(job).IsSome);
    }

    [Fact]
    public async Task APullRequestFromAnotherCloneIsNotAdoptedEvenWhenTheAgentReportsItLastAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (job, worktree) = await ReviewedEditAsync(run, "mentions");
        var own = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");

        await run.Repository.CommitToRemoteAsync("elsewhere", "OTHER.md", "Another clone's work.\n", Cancellation);
        var other = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("elsewhere", "main", "Another change", Cancellation);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {PullRequestLink(own)}, next to {PullRequestLink(other)}", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((own, "fix/rounding"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
    }

    [Fact]
    public async Task AColleaguesPullRequestFetchedIntoThisCloneIsNotAdoptedEvenWhenTheAgentReportsItLastAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (job, worktree) = await ReviewedEditAsync(run, "mentions");
        var own = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");

        await run.Repository.CommitToRemoteAsync("colleague", "OTHER.md", "A colleague's work.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "fetch", "--quiet", "origin", "colleague:colleague", "+refs/heads/colleague:refs/remotes/origin/colleague");
        var other = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("colleague", "main", "A colleague's change", Cancellation);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {PullRequestLink(own)}, next to {PullRequestLink(other)}", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((own, "fix/rounding"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
    }

    [Fact]
    public async Task APullRequestWhoseHeadOnlySharesItsNameWithALocalBranchIsNotAdoptedEvenWhenTheAgentReportsItLastAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (job, worktree) = await ReviewedEditAsync(run, "mentions");
        var own = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");

        await run.Repository.CommitToRemoteAsync("elsewhere", "OTHER.md", "Another clone's work.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "branch", "elsewhere", "origin/main");
        var other = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("elsewhere", "main", "Another change", Cancellation);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {PullRequestLink(own)}, next to {PullRequestLink(other)}", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);

        Assert.Equal((own, "fix/rounding"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
    }

    [Fact]
    public async Task APullRequestAnotherJobOfThisCloneDeliveredIsNotAdoptedByAJobWhoseAgentOnlyNamesItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var (first, worktree) = await ReviewedEditAsync(run, "mentions");
        var number = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(first, $"Opened {PullRequestLink(number)}", Cancellation));
        _ = await opened.UntilAsync(found => found.Job == first);
        var (second, _) = await ReviewedEditAsync(run, "mentions");
        var progressed = run.Watch<JobProgressed>();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(second, $"This builds on {PullRequestLink(number)}", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == second && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.True(run.Get<IPullRequests>().Of(second).IsNone);
    }

    [Fact]
    public async Task APullRequestLinkTheAgentWrapsInBackticksIsAdoptedAndWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ReviewedEditAsync(run, "mentions");
        var number = await PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");
        var progressed = run.Watch<JobProgressed>();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened `{PullRequestLink(number)}`.", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.Equal(Option<int>.Some(number), run.Get<IPullRequests>().Of(job).Map(watch => watch.PullRequest.Number));
    }

    [Fact]
    public async Task WithoutAUsableConnectionAPullRequestTheAgentReportsIsNamedOnceAboveItsConversationWithConnectAndNothingIsWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/permissions.json", Autonomous));
        _ = await run.Repository.PublishAsync(Cancellation);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("mentions"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        const string link = "https://forge.invalid/octo/shop/pulls/1";
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var note = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "AgentPullRequestNoteViewModel");

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {link}", Cancellation));

        await run.Ui.PresentedAsync(note.Presentation, () => note["IsShown"].Value<bool>(), () => "no note of the agent's pull request");
        Assert.Equal(
            "Pull request #1 was opened by the agent. Connect forge.invalid in Settings to follow its checks.",
            await run.Ui.ReadAsync(() => note["Text"].Text));
        Assert.True(run.Get<IPullRequests>().Of(job).IsNone);
    }

    internal static string PullRequestLink(int number) => $"https://forge.invalid/octo/shop/pulls/{number}";

    internal static async Task<int> PullRequestFromABranchNeverCheckedOutAsync(SimulatedRun run, WorkspaceInfo worktree, string branch, string fork = "")
    {
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", "--detach", "origin/main");
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "ROUNDING.md"), "Round half to even.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "add", "ROUNDING.md");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "-c", "user.name=Ada", "-c", "user.email=ada@localhost", "commit", "--quiet", "-m", "Round half to even");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "branch", branch, "HEAD");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", worktree.Branch);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", "origin", branch);

        return await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync(branch, "main", "Round half to even", Cancellation, fork: fork);
    }

    internal static async Task<(JobId Job, WorkspaceInfo Worktree)> ReviewedEditAsync(SimulatedRun run, string scenario = "edit")
    {
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate(scenario))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);

        return (job, Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation)));
    }

    [Fact]
    public async Task AFailedCheckOnTheJobsPullRequestShowsACardInItsConversationWhoseAskSendsTheAgentTheFailingChecksAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var changes = run.Watch<PullRequestWatchChanged>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation));
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", "origin", worktree.Branch);
        _ = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync(worktree.Branch, "main", "Edit the greeting", Cancellation, "ci-fails-once");
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Push your branch and open a pull request.", Cancellation));
        _ = await opened.UntilAsync(found => found.Job == job);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        run.AdvanceTo(Outcomes.Present(Outcomes.Present(run.Get<IPullRequests>().Of(job)).NextPoll));
        _ = await changes.UntilAsync(found => found.State.Job == job && found.State.Last.Match(last => last.Checks.Any(check => check.Status == CheckStatus.Failed), () => false));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);

        await workbench.ShowsAsync(() => conversation["Entries"].Items.Any(entry => entry.Kind == "CiViewModel" && entry["CanAsk"].Value<bool>()));
        var card = await run.Ui.ReadAsync(() => conversation["Entries"].Items.Single(entry => entry.Kind == "CiViewModel"));
        var (headline, request) = await run.Ui.ReadAsync(() => (card["Headline"].Text, card["Request"].Text));
        var progress = run.Watch<JobProgressed>();
        await run.Ui.InvokeAsync(() => card.Execute("AskCommand", request), Cancellation);
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.Running);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        Assert.Equal("CI failed: build", headline);
        Assert.Contains($"- build: {FailingTest}", request, StringComparison.Ordinal);
        Assert.Contains(request, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Attempts[^1].Guidance.Match(guidance => guidance, () => string.Empty), StringComparison.Ordinal);
    }
}
