using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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

    private static BoardJob Job(JobStatus status) =>
        new(new FakeCatalog().Add("Fix the failing test", status).Summary, Transcript.Empty);
}
