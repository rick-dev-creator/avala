using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Board;

public sealed class JobFactsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Preparing", "Running", "Starting")]
    [InlineData("Running", "Running", "Working")]
    [InlineData("Checking", "Running", "Verifying")]
    [InlineData("NeedsHelp", "NeedsYou", "NeedsHelp")]
    [InlineData("AwaitingReview", "ReadyForReview", "ReadyForReview")]
    [InlineData("Approved", "Done", "Approved")]
    [InlineData("Discarded", "Done", "Discarded")]
    [InlineData("Failed", "Done", "Failed")]
    public void EachStatusFallsInItsGroupWithItsFact(string status, string group, string fact)
    {
        var job = Job(Enum.Parse<JobStatus>(status));

        Assert.Equal((Enum.Parse<JobGroup>(group), Enum.Parse<FactKind>(fact)), (job.Group, job.Fact.Kind));
    }

    [Fact]
    public void ARunningJobThatAsksAPersonNeedsYouAndSaysWhatItAsks()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(session, turn), Now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, Now));

        var job = Job(JobStatus.Running) with { Transcript = transcript };

        Assert.Equal((JobGroup.NeedsYou, FactKind.AsksPermission, Option<ItemKind>.Some(ItemKind.Command), 1), (job.Group, job.Fact.Kind, job.Fact.Item, job.PendingDecisions));
    }

    [Fact]
    public void AHeldJobSaysWhyItWasHeld()
    {
        var job = Job(JobStatus.NeedsHelp) with { Hold = HoldReason.Stalled };

        Assert.Equal((FactKind.Held, Option<HoldReason>.Some(HoldReason.Stalled)), (job.Fact.Kind, job.Fact.Hold));
    }

    [Fact]
    public void AJobWaitingForItsUsageWindowToResetWaitsWithoutNeedingYouAndSaysWhenItResumes()
    {
        var job = Waiting(Now.AddHours(1));

        Assert.Equal((JobGroup.Waiting, FactKind.ResumesAtReset, job.Wait), (job.Group, job.Fact.Kind, job.Fact.Wait));
    }

    [Fact]
    public void AHeldJobWhoseResetTimeIsUnknownStillNeedsYou()
    {
        var job = Waiting(Option<DateTimeOffset>.None);

        Assert.Equal((JobGroup.NeedsYou, FactKind.Held), (job.Group, job.Fact.Kind));
    }

    [Fact]
    public void ASubAgentWhoseRequestWaitsForItsParentSaysSoInsteadOfWorking()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(session, turn), Now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToParent, Now));

        var job = Job(JobStatus.Running) with { Transcript = transcript };

        Assert.Equal((JobGroup.Running, FactKind.WaitsForParent, 0), (job.Group, job.Fact.Kind, job.PendingDecisions));
    }

    [Fact]
    public void AJobAwaitingReviewSaysOnWhichAttemptItWasVerified()
    {
        var report = new VerificationReport(JobId.New(), 2, VerificationOutcome.Passed, Option<Avala.Workspaces.Contracts.FileOrigin>.None, [], GateVerdict.Pass, Now);

        var job = Job(JobStatus.AwaitingReview) with { Verification = report };

        Assert.Equal((FactKind.Verified, 2), (job.Fact.Kind, job.Fact.Attempt));
    }

    [Fact]
    public void ARunningJobWithAPlanShowsItsProgress()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var transcript = Transcript.Empty.Apply(
            new PlanUpdated(session, turn, [new PlanStep("Write", PlanStepStatus.Done), new PlanStep("Test", PlanStepStatus.InProgress)]),
            Now);

        var job = Job(JobStatus.Running) with { Transcript = transcript };

        Assert.Equal((FactKind.PlanProgress, 1, 2), (job.Fact.Kind, job.Fact.Done, job.Fact.Total));
    }

    [Fact]
    public void ARunningJobWhosePlanWasEmptiedIsWorking()
    {
        var job = Job(JobStatus.Running) with { Transcript = Transcript.Empty.Apply(new PlanUpdated(SessionId.New(), TurnId.New(), []), Now) };

        Assert.Equal(FactKind.Working, job.Fact.Kind);
    }

    private static BoardJob Waiting(Option<DateTimeOffset> resumesAt)
    {
        var job = Job(JobStatus.NeedsHelp);

        return job with { Hold = HoldReason.LimitNearlyReached, Wait = new ResetWait(job.Job, new ConnectionName("claude-work"), "5h", resumesAt, Now) };
    }

    [Theory]
    [InlineData("Watching", null, "Delivered", "Delivered")]
    [InlineData("Green", null, "Delivered", "Delivered")]
    [InlineData("Ended", "Merged", "Done", "PullRequestMerged")]
    [InlineData("Ended", "Closed", "Done", "PullRequestClosed")]
    [InlineData("Ended", "Green", "Done", "Approved")]
    public void AnApprovedJobsPullRequestKeepsItDeliveredWhileItIsWatchedAndSaysHowItEnded(string status, string? ended, string group, string fact)
    {
        var watch = new Forges.Contracts.PullRequestWatchState(JobId.New(), new Forges.Contracts.ForgeName("local"), new Forges.Contracts.PullRequestRef(7, new Uri("https://forge.example/o/r/pulls/7"), "avala/7", "main"), Forges.Contracts.OnPullRequest.WatchOnly, 3)
        {
            Status = Enum.Parse<Forges.Contracts.WatchStatus>(status),
            Ended = ended is null ? Option<Forges.Contracts.WatchEnd>.None : Enum.Parse<Forges.Contracts.WatchEnd>(ended),
            Last = new Forges.Contracts.PullRequestState(new Forges.Contracts.PullRequestRef(7, new Uri("https://forge.example/o/r/pulls/7"), "avala/7", "main"), Forges.Contracts.PullRequestLifecycle.Open, "abc", Forges.Contracts.Mergeability.Mergeable)
            {
                Checks = [new Forges.Contracts.CheckRun("build", Forges.Contracts.CheckStatus.Passed, Option<Uri>.None, string.Empty), new Forges.Contracts.CheckRun("lint", Forges.Contracts.CheckStatus.Failed, Option<Uri>.None, string.Empty)],
            },
        };

        var job = Job(JobStatus.Approved) with { PullRequest = watch };

        Assert.Equal((Enum.Parse<JobGroup>(group), Enum.Parse<FactKind>(fact)), (job.Group, job.Fact.Kind));
        Assert.Equal(fact == "Approved" ? (0, 0, 0) : (7, fact == "Delivered" ? 1 : 0, fact == "Delivered" ? 2 : 0), (job.Fact.PullRequest, job.Fact.Done, job.Fact.Total));
    }

    private static BoardJob Job(JobStatus status) =>
        new(new FakeCatalog().Add("Fix the failing test", status).Summary, Transcript.Empty);
}
