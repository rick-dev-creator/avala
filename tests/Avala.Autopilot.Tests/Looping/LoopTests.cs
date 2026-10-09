using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Looping;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Verification.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Autopilot.Tests.Looping;

public sealed class LoopTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABacklogOfCleanJobsRunsOneJobAtATimeEachApprovedAutomaticallyUntilItIsDrainedAsync()
    {
        await using var pilot = new Pilot();
        var greet = pilot.Backlog.Add("greet", "Greet the team");
        var docs = pilot.Backlog.Add("docs", "Document the greeting");
        var loop = await pilot.StartAsync();

        await pilot.TakenAsync(1);
        Assert.Single(pilot.Jobs.Submitted);
        var first = await pilot.CleanAsync(1);
        var second = await pilot.CleanAsync(2, "README.md");
        var ended = await pilot.EndedAsync();

        Assert.Equal(["Greet the team", "Document the greeting"], pilot.Jobs.Submitted.Select(submitted => submitted.Request.Instruction));
        Assert.Equal(pilot.Jobs.Submitted.Select(submitted => submitted.Job), pilot.Jobs.Approved);
        Assert.All([first, second], iteration => Assert.Equal(IterationOutcome.ApprovedAutomatically, iteration.Outcome));
        Assert.Equal((LoopStatus.Ended, Option<LoopEnding>.Some(LoopEnding.Drained), 2), (ended.Status, ended.Ending, ended.Iterations));
        Assert.Equal(
            [(greet, TaskState.Taken), (greet, TaskState.Approved), (docs, TaskState.Taken), (docs, TaskState.Approved)],
            pilot.Backlog.Marks.Select(marked => (marked.Task, marked.Mark.State)));
        var decided = Assert.Single(pilot.Bus.Published.OfType<AutoApprovalDecided>(), approval => approval.Decision.Job == second.Job.Match(job => job, () => default));
        Assert.Equal((true, Option<string>.Some("c0ffee"), "README.md"), (decided.Decision.Approved, decided.Decision.Delivery.Bind(delivery => delivery.Commit), string.Join(',', decided.Decision.Evidence.FilesChanged)));
        Assert.Equal(loop, Assert.Single(pilot.Registry.Loops()).Loop);
    }

    [Fact]
    public async Task AJobWithAnExceptionWaitsForAPersonWithItsReasonsAndTheLoopMovesOnAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("checks", "Loosen the checks");
        pilot.Backlog.Add("greet", "Greet the team");
        await pilot.StartAsync();

        var exceptional = await pilot.CleanAsync(1, ".avala/checks.json", "calculator.txt");
        var clean = await pilot.CleanAsync(2);

        Assert.Equal((IterationOutcome.AwaitingReview, "RuleFileEdited"), (exceptional.Outcome, string.Join(',', exceptional.Exceptions)));
        Assert.Equal(IterationOutcome.ApprovedAutomatically, clean.Outcome);
        Assert.Equal([clean.Job.Match(job => job, () => default)], pilot.Jobs.Approved);
        var refused = Assert.Single(pilot.Bus.Published.OfType<AutoApprovalDecided>(), decided => !decided.Decision.Approved).Decision;
        Assert.Equal([ExceptionReason.RuleFileEdited], refused.Exceptions);
        Assert.Contains(pilot.Backlog.Marks, marked => marked.Task.Key == "checks" && marked.Mark.State == TaskState.WaitingForPerson);
    }

    [Fact]
    public async Task ARefusedDeliveryLeavesTheJobWaitingForAPersonWithTheStrategysRejectionAsync()
    {
        await using var pilot = new Pilot();
        pilot.Jobs.ApprovalRefusal = JobRejection.MergeConflict;
        pilot.Backlog.Add("greet", "Greet the team");
        await pilot.StartAsync();

        var iteration = await pilot.CleanAsync(1);

        Assert.Equal(
            (IterationOutcome.AwaitingReview, "DeliveryRefused", Option<JobRejection>.Some(JobRejection.MergeConflict)),
            (iteration.Outcome, string.Join(',', iteration.Exceptions), iteration.Rejection));
    }

    [Fact]
    public async Task TheSameFailureRepeatedTripsTheBreakerAndLeavesTheRestOfTheBacklogAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Fix the sum");
        pilot.Backlog.Add("two", "Fix the product");
        pilot.Backlog.Add("three", "Fix the division");
        await pilot.StartAsync();

        var first = await pilot.FailingAsync(1, "tests", 1);
        await pilot.FailingAsync(2, "tests", 1);
        var ended = await pilot.EndedAsync();

        Assert.Equal(Option<FailureSignature>.Some(new FailureSignature(FailureSource.Check, "tests Failed exit 1")), first.Failure);
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.BreakerTripped), Option<Breaker>.Some(Breaker.SameFailure)), (ended.Ending, ended.Breaker));
        Assert.Equal(2, pilot.Jobs.Submitted.Count);
        Assert.Equal(new BreakerTrip(Breaker.SameFailure, "Check tests Failed exit 1", 2, 2, pilot.Clock.GetUtcNow()), Assert.Single(pilot.Bus.Published.OfType<BreakerTripped>()).Trip);
    }

    [Fact]
    public async Task ARejectedSubmissionCountsAsAFailureAndMarksTheTaskFailedAsync()
    {
        await using var pilot = new Pilot();
        pilot.Jobs.Rejection = JobRejection.UnknownConnection;
        pilot.Backlog.Add("one", "Greet the team");
        await pilot.StartAsync(new LoopLimits { FailuresInARow = 1 });

        var iteration = await pilot.IteratedAsync(1);
        var ended = await pilot.EndedAsync();

        Assert.Equal((IterationOutcome.NotSubmitted, Option<JobRejection>.Some(JobRejection.UnknownConnection)), (iteration.Outcome, iteration.Rejection));
        Assert.Equal(Option<Breaker>.Some(Breaker.FailuresInARow), ended.Breaker);
        Assert.Equal([TaskState.Failed], pilot.Backlog.Marks.Select(marked => marked.Mark.State));
    }

    [Fact]
    public async Task AConnectionAtItsLimitThresholdPausesTheLoopUntilTheWindowResetsThenResumesItAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        await pilot.StartAsync();
        var resets = pilot.Clock.GetUtcNow().AddHours(2);
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.95, resets)];
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");

        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        var paused = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);
        Assert.Single(pilot.Jobs.Submitted);
        pilot.Clock.Advance(TimeSpan.FromHours(2));
        var resumed = await pilot.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);
        await pilot.TakenAsync(2);

        Assert.Equal(new LoopPause(PauseReason.UsageLimit, resets.AddHours(-2), resets) { Window = "5h" }, paused.Pause);
        Assert.Equal(resets, resumed.At);
        Assert.Equal(2, pilot.Jobs.Submitted.Count);
    }

    [Fact]
    public async Task ALoopNamingNoConnectionGoesOnWhileAnotherConnectionOfAnyProviderHasCapacityAsync()
    {
        await using var pilot = new Pilot();
        pilot.Connections.Names.Add(new ConnectionName("personal"));
        pilot.Connections.Providers[new ConnectionName("personal")] = "another-harness";
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.95, pilot.Clock.GetUtcNow().AddHours(2))];
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");

        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        await pilot.TakenAsync(2);

        Assert.Empty(pilot.Bus.Published.OfType<LoopPaused>());
        Assert.All(pilot.Jobs.Submitted, submitted => Assert.True(submitted.Request.Connection.IsNone));
    }

    [Fact]
    public async Task ALoopNamingNoConnectionPausesWhenTheFixedMachineDefaultIsAtItsLimitWhateverTheOthersHaveLeftAsync()
    {
        await using var pilot = new Pilot();
        pilot.Connections.Names.Add(new ConnectionName("personal"));
        pilot.Connections.Fixed = new ConnectionName("work");
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.95, pilot.Clock.GetUtcNow().AddHours(2))];
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");

        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        var paused = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);

        Assert.Equal((PauseReason.UsageLimit, Option<string>.Some("5h")), (paused.Pause.Reason, paused.Pause.Window));
        Assert.Single(pilot.Jobs.Submitted);
    }

    [Fact]
    public async Task AJobHeldNearItsLimitPausesTheLoopAndIsContinuedOnceTheWindowResetsAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        await pilot.StartAsync(new LoopLimits { PauseAtLimit = 0.99 });
        var resets = pilot.Clock.GetUtcNow().AddMinutes(30);
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.91, resets)];
        pilot.Jobs.Attempts(job, Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted));

        await pilot.ProgressAsync(job, JobStatus.NeedsHelp);
        await pilot.Feed.HandleAsync(new JobHeld(new JobHold(job, default, HoldReason.LimitNearlyReached, SessionHalt.Interrupted)), Cancellation);
        var paused = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);
        pilot.Clock.Advance(TimeSpan.FromMinutes(30));
        await pilot.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);

        Assert.Equal(Option<DateTimeOffset>.Some(resets), paused.Pause.Until);
        Assert.Equal([(job, LoopRunner.ResetMessage)], pilot.Jobs.Continued);
        Assert.Empty(pilot.Bus.Published.OfType<LoopIterated>());
    }

    [Fact]
    public async Task ALimitAtItsThresholdWithoutAResetTimeTripsTheBreakerAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("weekly", 0.97, Option<DateTimeOffset>.None)];

        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");
        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        var ended = await pilot.EndedAsync();

        Assert.Equal(Option<Breaker>.Some(Breaker.UsageLimit), ended.Breaker);
    }

    [Fact]
    public async Task ARecurringTaskNotYetDueMakesTheLoopWaitUntilItIsDueAsync()
    {
        var recurring = new MemorySource("recurring");
        await using var pilot = new Pilot(recurring);
        var due = pilot.Clock.GetUtcNow().AddHours(1);
        recurring.NextDue = due;
        await pilot.StartAsync();

        var waiting = await pilot.Bus.WaitForAsync<LoopWaiting>(_ => true, Cancellation);
        Assert.Equal(LoopStatus.Waiting, Assert.Single(pilot.Registry.Loops()).Status);
        recurring.NextDue = Option<DateTimeOffset>.None;
        recurring.Add("deps", "Update the dependencies");
        pilot.Clock.Advance(TimeSpan.FromHours(1));
        await pilot.TakenAsync(1);

        Assert.Equal(due, waiting.Until);
        Assert.Equal(["Update the dependencies"], pilot.Jobs.Submitted.Select(submitted => submitted.Request.Instruction));
    }

    [Fact]
    public async Task ARequestLeftToAPersonMakesTheJobWaitAndTheLoopMovesOnAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Migrate the database");
        pilot.Backlog.Add("two", "Greet the team");
        await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);

        await pilot.Feed.HandleAsync(new PermissionDecided(Decision(job)), Cancellation);
        var iteration = await pilot.IteratedAsync(1);
        await pilot.TakenAsync(2);

        Assert.Equal(IterationOutcome.AwaitingAnswer, iteration.Outcome);
    }

    [Fact]
    public async Task ASourceThatCannotBeReadEndsTheLoopWithItsErrorAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Error = AutopilotError.DuplicateKey;
        await pilot.StartAsync();

        var ended = await pilot.EndedAsync();

        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.SourceFailed), Option<AutopilotError>.Some(AutopilotError.DuplicateKey)), (ended.Ending, ended.Error));
    }

    [Fact]
    public async Task ACommandWhoseWorkFailsEndsTheLoopAsFailedAndAnswersThatItEndedAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.NextDue = pilot.Clock.GetUtcNow().AddHours(1);
        var loop = await pilot.StartAsync();
        _ = await pilot.Bus.WaitForAsync<LoopWaiting>(_ => true, Cancellation);
        Assert.Equal(loop, Outcomes.Succeeds(await pilot.Registry.PauseAsync(loop, Cancellation)));
        pilot.Backlog.Fault = new InvalidOperationException("The ledger is closed");

        var answer = await pilot.Registry.ResumeAsync(loop, Cancellation);
        var ended = await pilot.EndedAsync();

        Assert.Equal(AutopilotError.LoopEnded, Outcomes.FailsWith(answer));
        Assert.Equal(
            (LoopStatus.Ended, Option<LoopEnding>.Some(LoopEnding.Failed), Option<string>.Some("InvalidOperationException: The ledger is closed")),
            (ended.Status, ended.Ending, ended.Fault));
    }

    [Fact]
    public async Task APausedLoopTakesNoNewTaskUntilResumedAndAStoppedLoopTakesNoneAgainAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Document the greeting");
        pilot.Backlog.Add("three", "Announce the greeting");
        var loop = await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);

        Assert.Equal(loop, Outcomes.Succeeds(await pilot.Registry.PauseAsync(loop, Cancellation)));
        Assert.Equal(AutopilotError.NotRunning, Outcomes.FailsWith(await pilot.Registry.PauseAsync(loop, Cancellation)));
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");
        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        await pilot.IteratedAsync(1);
        Assert.Equal((LoopStatus.Paused, 1), (Assert.Single(pilot.Registry.Loops()).Status, pilot.Jobs.Submitted.Count));
        Assert.Equal(loop, Outcomes.Succeeds(await pilot.Registry.ResumeAsync(loop, Cancellation)));
        await pilot.TakenAsync(2);
        Assert.Equal(AutopilotError.NotPaused, Outcomes.FailsWith(await pilot.Registry.ResumeAsync(loop, Cancellation)));
        Assert.Equal(loop, Outcomes.Succeeds(await pilot.Registry.StopAsync(loop, Cancellation)));

        var ended = await pilot.EndedAsync();
        Assert.Equal(Option<LoopEnding>.Some(LoopEnding.Stopped), ended.Ending);
        Assert.Equal(AutopilotError.LoopEnded, Outcomes.FailsWith(await pilot.Registry.StopAsync(loop, Cancellation)));
        Assert.Equal(AutopilotError.UnknownLoop, Outcomes.FailsWith(await pilot.Registry.PauseAsync(LoopId.New(), Cancellation)));
        Assert.Equal(2, pilot.Jobs.Submitted.Count);
    }

    [Fact]
    public async Task ARepositoryRunsOneLoopAtATimeAndARequestNeedsARepositoryAndValidLimitsAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        var loop = await pilot.StartAsync();
        await pilot.TakenAsync(1);

        Assert.Equal(AutopilotError.AlreadyRunning, Outcomes.FailsWith(await pilot.Registry.StartAsync(new LoopRequest(Pilot.Repository + "/"), Cancellation)));
        Assert.Equal(AutopilotError.EmptyRepository, Outcomes.FailsWith(await pilot.Registry.StartAsync(new LoopRequest(" "), Cancellation)));
        Assert.Equal(
            AutopilotError.InvalidLimits,
            Outcomes.FailsWith(await pilot.Registry.StartAsync(new LoopRequest("/elsewhere") { Limits = new LoopLimits { PauseAtLimit = 0 } }, Cancellation)));
        _ = await pilot.Registry.StopAsync(loop, Cancellation);
        await pilot.EndedAsync();
        Outcomes.Succeeds(await pilot.Registry.StartAsync(new LoopRequest(Pilot.Repository), Cancellation));
    }

    [Fact]
    public async Task TheDigestHoldsWhatWasApprovedWithItsEvidenceWhatWaitsAndWhyTheCostTheBreakersAndThePausesAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("one", "Greet the team");
        pilot.Backlog.Add("two", "Loosen the checks");
        var loop = await pilot.StartAsync(new LoopLimits { SpendPerLoop = [Pilot.Usd(0.05m)] });
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Spent(job, Pilot.Usd(0.03m));
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");
        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);
        var exceptional = await pilot.TakenAsync(2);
        pilot.Usage.Spent(exceptional, Pilot.Usd(0.02m));

        pilot.Passed(exceptional);
        pilot.Work.Changed(exceptional, ".avala/checks.json");
        await pilot.ProgressAsync(exceptional, JobStatus.AwaitingReview);
        await pilot.EndedAsync();

        var digest = Outcomes.Present(pilot.Registry.DigestOf(loop));
        Assert.Equal([true, false], digest.Approvals.Select(approval => approval.Approved));
        var evidence = digest.Approvals[0].Evidence;
        Assert.Equal(
            (1, Option<VerificationOutcome>.Some(VerificationOutcome.Passed), "tests", "GREETING.md"),
            (evidence.Attempts, evidence.Verification, string.Join(',', evidence.ChecksPassed), string.Join(',', evidence.FilesChanged)));
        Assert.Equal(
            [(IterationOutcome.ApprovedAutomatically, ""), (IterationOutcome.AwaitingReview, "RuleFileEdited")],
            digest.Iterations.Select(iteration => (iteration.Outcome, string.Join(',', iteration.Exceptions))));
        Assert.Equal([Pilot.Usd(0.05m)], digest.Spent);
        Assert.Equal([Breaker.SpendPerLoop], digest.Breakers.Select(trip => trip.Breaker));
        Assert.Empty(digest.Pauses);
    }

    [Fact]
    public async Task TheSpendingOfTheTrailingWindowIsReadFromTheStoredHistoryAndTripsItsBreakerAsync()
    {
        await using var pilot = new Pilot();
        pilot.Usage.Window = [Pilot.Usd(12m)];
        pilot.Backlog.Add("one", "Greet the team");
        await pilot.StartAsync(new LoopLimits { SpendPerWindow = [Pilot.Usd(10m)], Window = TimeSpan.FromHours(24) });

        var ended = await pilot.EndedAsync();

        Assert.Equal(Option<Breaker>.Some(Breaker.SpendPerWindow), ended.Breaker);
        Assert.Equal((pilot.Clock.GetUtcNow().AddHours(-24), pilot.Clock.GetUtcNow()), Assert.Single(pilot.Usage.Windows));
        Assert.Empty(pilot.Jobs.Submitted);
    }

    private static PolicyDecision Decision(JobId job) =>
        new(default, default, new Agents.Contracts.Sessions.ItemId("migrate"), job, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);
}
