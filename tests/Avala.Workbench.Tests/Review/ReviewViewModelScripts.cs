using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Review;
using Avala.Workbench.Steering;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Review;

public sealed class ReviewViewModelScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly JobSummary job;

    public ReviewViewModelScripts() => job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSheetShowsTheVerdictFirstThenTheExceptionsTheQuietLineAndTheDiff()
    {
        var failing = new CheckEvidence("calculator", "git grep", CheckStatus.Failed, 1, TimeSpan.FromSeconds(1), "add(2, 2) = 5", string.Empty);
        bench.Evidence.Runs[job.Job] = new RunEvidence(job.Job, new EvidenceSummary(2, VerificationOutcome.Passed, [], 2, 0, []), [])
        {
            Verifications = [Report(1, VerificationOutcome.Failed, failing), Report(2, VerificationOutcome.Passed, Passing("calculator"))],
            AllowedByRules = 2,
        };
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);
        bench.Changes.Files = [new FileChange("calculator.txt", ChangeKind.Added, 1, 0)];
        var review = Review();

        await review.Request(0)(Cancellation);

        Assert.Equal(("Verified on attempt 2 of 2", "calculator passed in the worktree after the last turn.", true), (review.Verdict, review.Proof, review.IsVerified));
        var exception = Assert.Single(review.Exceptions);
        Assert.Equal(("Attempt 1 failed", "calculator · exit 1", "add(2, 2) = 5", ExceptionTone.Failure), (exception.Title, exception.Fact, exception.Output, exception.Tone));
        Assert.Equal("2 other decisions were allowed by rules · 0.25 USD · 1,500 tokens", review.Quiet);
        Assert.Equal(("1 file changed", "+1", "calculator.txt", "+1"), (review.Changes, review.Totals, review.Files[0].Path, review.Files[0].Counts));
    }

    [Fact]
    public void TheSheetShowsHowFarTheJobsPlanGotAndWhichStepsAreLeft()
    {
        var session = Agents.Contracts.Sessions.SessionId.New();
        var turn = Agents.Contracts.Sessions.TurnId.New();
        var planned = Workbench.Timeline.Transcript.Empty.Apply(new TurnStarted(session, turn), DateTimeOffset.UnixEpoch).Apply(
            new PlanUpdated(session, turn, [new PlanStep("Read the code", PlanStepStatus.Done), new PlanStep("Write the change", PlanStepStatus.InProgress), new PlanStep("Run the tests", PlanStepStatus.Pending)]),
            DateTimeOffset.UnixEpoch);

        ViewModelScript.Given(Review())
            .When(review => review.Track(new Workbench.Board.BoardJob(job, planned)))
            .ThenNotified(nameof(ReviewViewModel.Plan), nameof(ReviewViewModel.PlanLeft))
            .Then(review => Assert.Equal(
                ("Plan · 1 of 3 steps done", "InProgress · Write the change | Pending · Run the tests", JobStatus.AwaitingReview),
                (review.Plan, string.Join(" | ", review.PlanLeft.Select(step => $"{step.Status} · {step.Title}")), review.Status)))
            .When(review => review.Track(new Workbench.Board.BoardJob(job, Workbench.Timeline.Transcript.Empty)))
            .Then(review => Assert.Equal((string.Empty, 0), (review.Plan, review.PlanLeft.Count)));
    }

    [Fact]
    public async Task TheHeaderNamesTheJobWhereItRunsAndWhereItStands()
    {
        var review = Review();

        await review.Request(0)(Cancellation);

        Assert.Equal(("Ready for review", "Rate-limit POST /login"), (review.Heading, review.Title));
        Assert.Equal("repo · 0 sessions", review.Facts);
    }

    [Fact]
    public async Task AFailedVerdictIsNotDrawnAsVerified()
    {
        bench.Evidence.Runs[job.Job] = Evidence(Report(1, VerificationOutcome.Failed, new CheckEvidence("test", "go test", CheckStatus.Failed, 1, TimeSpan.Zero, "FAIL", string.Empty)));
        var review = Review();

        await review.Request(0)(Cancellation);

        Assert.Equal(("Not verified: attempt 1 of 1 failed its checks", "test failed on the last attempt.", false), (review.Verdict, review.Proof, review.IsVerified));
    }

    [Fact]
    public async Task OnlyTheFirstExceptionStartsOpenAndWhatTheReviewerOpenedStaysOpenWhenTheSheetReloads()
    {
        bench.Evidence.Runs[job.Job] = Evidence() with { RuleFiles = [".avala/checks.json", ".avala/permissions.json"] };
        var review = Review();
        await review.Request(0)(Cancellation);
        var first = review.Exceptions.Select(exception => exception.IsExpanded).ToList();
        review.Exceptions[0].IsExpanded = false;
        review.Exceptions[1].IsExpanded = true;

        await review.Request(1)(Cancellation);

        Assert.Equal([true, false], first);
        Assert.Equal([false, true], review.Exceptions.Select(exception => exception.IsExpanded));
    }

    [Fact]
    public async Task ALoadForARevisionOlderThanTheLatestRequestIsDropped()
    {
        var review = Review();
        var older = review.Request(0);
        var newer = review.Request(1);

        await older(Cancellation);
        var afterOlder = review.IsLoaded;
        await newer(Cancellation);

        Assert.Equal((false, true), (afterOlder, review.IsLoaded));
    }

    [Fact]
    public async Task AFileShowsItsHunksOnDemand()
    {
        bench.Changes.Files = [new FileChange("calculator.txt", ChangeKind.Modified, 1, 1)];
        bench.Changes.Hunks["calculator.txt"] = new FileDiff("calculator.txt", false, [new DiffHunk(1, 1, 1, 1, string.Empty, [new DiffLine(DiffLineKind.Removed, "add(2, 2) = 5"), new DiffLine(DiffLineKind.Added, "add(2, 2) = 4")])]);
        var review = Review();
        await review.Request(0)(Cancellation);
        var file = review.Files[0];
        var before = file.Hunks.Count;

        await file.ShowHunksCommand.ExecuteAsync(null);

        Assert.Equal(0, before);
        Assert.Equal(["-add(2, 2) = 5", "+add(2, 2) = 4"], Assert.Single(file.Hunks).Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task AnApprovalRefusedForAConflictShowsTheConflictingFilesInPlaceAndCanBeTriedAgain()
    {
        bench.Jobs.Refusal = JobRejection.MergeConflict;
        bench.Changes.Conflicts = ["calculator.txt"];
        var review = Review();

        await review.ApproveCommand.ExecuteAsync(null);

        Assert.Equal(("The work conflicts with the base branch in calculator.txt.", "calculator.txt"), (review.Refusal, string.Join(",", review.Conflicts)));
        Assert.Equal((false, string.Empty, true), (review.IsClosed, review.Outcome, review.ApproveCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AnApprovedJobShowsHowItWasDeliveredAndClosesTheSheet()
    {
        var review = Review();

        var script = ViewModelScript.Given(review);

        await review.ApproveCommand.ExecuteAsync(null);

        script.ThenNotified(nameof(ReviewViewModel.Outcome), nameof(ReviewViewModel.IsClosed), nameof(ReviewViewModel.Heading));

        Assert.Equal(("Approved: the branch avala/fix-the-test is ready", true, "Approved"), (review.Outcome, review.IsClosed, review.Heading));
    }

    [Fact]
    public async Task ARepositoryWithAUsableForgeIsOfferedAPullRequestThatApprovesThroughItAsync()
    {
        bench.PullRequests.Offer = new Forges.Contracts.PullRequestOffer(new Forges.Contracts.ForgeName("github"), "GitHub", "origin");
        var review = Review();

        await review.Request(0)(Cancellation);
        await review.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("Open pull request on github (GitHub, origin)", review.PullRequestOffer);
        Assert.Equal(["approve through pull-request"], bench.Jobs.Calls);
        Assert.Equal(("Pushed avala/fix-the-test and opened its pull request; the inspector follows its checks", "Pull request opened"), (review.Outcome, review.Heading));
    }

    [Fact]
    public async Task WithoutAUsableForgeNoPullRequestIsOfferedAsync()
    {
        var review = Review();

        await review.Request(0)(Cancellation);

        Assert.Equal((string.Empty, false, true), (review.PullRequestOffer, review.OpenPullRequestCommand.CanExecute(null), review.ApproveCommand.CanExecute(null)));
    }

    [Fact]
    public async Task APullRequestTheForgeRefusedSaysWhyAndTheJobStillAwaitsReviewAsync()
    {
        bench.PullRequests.Offer = new Forges.Contracts.PullRequestOffer(new Forges.Contracts.ForgeName("github"), "GitHub", "origin");
        bench.PullRequests.Refusals[job.Job] = Forges.Contracts.ForgeError.MissingCredential;
        bench.Jobs.Refusal = JobRejection.DeliveryFailed;
        var review = Review();
        await review.Request(0)(Cancellation);

        await review.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("The pull request was not opened: the token's environment variable is not set. The job still awaits review.", review.Refusal);
        Assert.Equal((false, true), (review.IsClosed, review.OpenPullRequestCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ApprovingTwiceDeliversTheJobOnce()
    {
        var review = Review();

        await review.ApproveCommand.ExecuteAsync(null);
        var again = review.ApproveCommand.CanExecute(null);
        await review.ApproveCommand.ExecuteAsync(null);

        Assert.False(again);
        Assert.Equal(["approve"], bench.Jobs.Calls);
        Assert.Equal("Approved: the branch avala/fix-the-test is ready", review.Outcome);
    }

    [Fact]
    public async Task WhileAnApprovalIsInFlightNothingElseCanBeDecided()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bench.Jobs.Gate = gate;
        var review = Review();
        review.Feedback = "Also limit by account";
        review.RequestDiscardCommand.Execute(null);
        review.CancelDiscardCommand.Execute(null);

        var approving = review.ApproveCommand.ExecuteAsync(null);
        var during = (review.ApproveCommand.CanExecute(null), review.SendBackCommand.CanExecute(null), review.RequestDiscardCommand.CanExecute(null));
        gate.SetResult();
        await approving;

        Assert.Equal((false, false, false), during);
        Assert.Equal(["approve"], bench.Jobs.Calls);
    }

    [Fact]
    public async Task AReloadArrivingWhileApprovingRefreshesTheProofAndTheApprovalStillLands()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bench.Jobs.Gate = gate;
        var review = Review();
        await review.Request(0)(Cancellation);
        var approving = review.ApproveCommand.ExecuteAsync(null);
        bench.Changes.Files = [new FileChange("calculator.txt", ChangeKind.Modified, 2, 1)];

        await review.Request(1)(Cancellation);
        review.Track(JobStatus.Approved);
        gate.SetResult();
        await approving;

        Assert.Equal(("1 file changed", "Approved: the branch avala/fix-the-test is ready", true), (review.Changes, review.Outcome, review.IsClosed));
        Assert.Empty(review.Refusal);
    }

    [Fact]
    public void AJobApprovedElsewhereClosesTheSheetWithTheStamp() =>
        ViewModelScript.Given(Review())
            .When(review => review.Track(JobStatus.Approved))
            .ThenNotified(nameof(ReviewViewModel.IsClosed), nameof(ReviewViewModel.Heading))
            .Then(review => Assert.Equal((true, "Approved", "Approved"), (review.IsClosed, review.Outcome, review.Heading)));

    [Fact]
    public async Task SendingBackTakesFeedbackOnlyWhileTheJobAwaitsReview()
    {
        var review = Review();
        var empty = review.SendBackCommand.CanExecute(null);
        review.Feedback = " Greet the new hires too ";

        await review.SendBackCommand.ExecuteAsync(null);
        review.Track(JobStatus.Running);
        review.Feedback = "Again";

        Assert.Equal((false, false), (empty, review.SendBackCommand.CanExecute(null)));
        Assert.Equal(["send back Greet the new hires too"], bench.Jobs.Calls);
        Assert.Equal(("Sent back with your feedback", "Sent back", true), (review.Outcome, review.Heading, review.IsClosed));
    }

    [Fact]
    public async Task AMessageQueuedWhileTheAgentWorkedIsOfferedAndSentBackOnlyWhenChosen()
    {
        bench.Jobs.Status = JobStatus.AwaitingReview;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await bench.Queue.DeliverAsync(job.Job, JobStatus.Running, "Keep the old namespace as an alias", Cancellation)));
        var review = Review();
        var before = review.SendBackQueuedCommand.CanExecute(null);

        await review.Request(0)(Cancellation);
        var shown = (review.Queued, review.SendBackQueuedCommand.CanExecute(null), bench.Jobs.Calls.Count);
        await review.SendBackQueuedCommand.ExecuteAsync(null);

        Assert.Equal((false, ("Keep the old namespace as an alias", true, 0)), (before, shown));
        Assert.Equal(["send back Keep the old namespace as an alias"], bench.Jobs.Calls);
        Assert.Equal((string.Empty, false, "Sent back with the message you queued"), (review.Queued, bench.Queue.Find(job.Job).IsSome, review.Outcome));
    }

    [Fact]
    public async Task DiscardingTakesARequestAndAConfirmation()
    {
        var review = Review();
        var direct = review.ConfirmDiscardCommand.CanExecute(null);

        review.RequestDiscardCommand.Execute(null);
        var whileAsked = review.ApproveCommand.CanExecute(null);
        review.CancelDiscardCommand.Execute(null);
        var cancelled = bench.Jobs.Calls.Count;
        review.RequestDiscardCommand.Execute(null);
        await review.ConfirmDiscardCommand.ExecuteAsync(null);

        Assert.Equal((false, false, 0, false), (direct, whileAsked, cancelled, review.ConfirmingDiscard));
        Assert.Equal(("Discarded", "Discarded", "discard"), (review.Outcome, review.Heading, string.Join(",", bench.Jobs.Calls)));
    }

    [Fact]
    public async Task AJobWithoutEvidenceOrWorktreeSaysSoAndIsLoaded()
    {
        var bare = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.NeedsHelp).Summary;
        var review = new ReviewViewModel(bare.Job, bench.Reader, bench.Desk, bench.Ui);

        await ViewModelScript.Given(review).WhenPresentedAsync(shown => _ = shown.Request(0)(Cancellation), Cancellation);

        Assert.Equal(("No evidence for this job", "The job has no worktree", false, true), (review.Verdict, review.Changes, review.HasExceptions, review.IsLoaded));
        Assert.Equal((string.Empty, false), (review.Totals, review.IsVerified));
        Assert.Empty(review.Files);
    }

    [Fact]
    public async Task AnExpandedFileStaysExpandedWhenTheSheetReloads()
    {
        bench.Changes.Files = [new FileChange("src/auth/login.ts", ChangeKind.Modified, 24, 3)];
        bench.Changes.Hunks["src/auth/login.ts"] = new FileDiff("src/auth/login.ts", false, [new DiffHunk(12, 6, 12, 14, "login", [new DiffLine(DiffLineKind.Added, "limiter.consume()")])]);
        var review = Review();
        await review.Request(0)(Cancellation);
        var file = review.Files[0];
        await file.ShowHunksCommand.ExecuteAsync(null);

        await review.Request(1)(Cancellation);

        Assert.Same(file, Assert.Single(review.Files));
        Assert.True(file.IsExpanded);
    }

    [Fact]
    public async Task AnApprovalRefusedForADirtyBaseCheckoutAsksToCommitOrStash()
    {
        bench.Jobs.Refusal = JobRejection.BaseCheckoutDirty;
        var review = Review();

        await review.ApproveCommand.ExecuteAsync(null);

        Assert.Equal(("The base branch's checkout has uncommitted changes. Commit or stash them, then approve again.", 0, false), (review.Refusal, review.Conflicts.Count, review.IsClosed));
    }

    [Fact]
    public async Task ASuccessfulRetryClearsTheRefusal()
    {
        bench.Jobs.Refusal = JobRejection.BaseCheckoutDirty;
        var review = Review();
        await review.ApproveCommand.ExecuteAsync(null);
        bench.Jobs.Refusal = (JobRejection)(-1);

        await review.ApproveCommand.ExecuteAsync(null);

        Assert.Equal((string.Empty, true), (review.Refusal, review.IsClosed));
    }

    [Fact]
    public void ClosingTheSheetTellsWhoeverHostsIt()
    {
        var review = Review();
        var closed = 0;
        review.Closed += (_, _) => closed++;

        review.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
    }

    [Fact]
    public void AnEndedJobCanNeitherBeApprovedNorDiscarded() =>
        ViewModelScript.Given(Review())
            .When(review => review.Track(JobStatus.Approved))
            .ThenNotified(nameof(ReviewViewModel.Status))
            .Then(review => Assert.Equal((false, false), (review.ApproveCommand.CanExecute(null), review.RequestDiscardCommand.CanExecute(null))));

    [Fact]
    public void AHeldJobCanBeDiscardedButNotApproved() =>
        ViewModelScript.Given(Review())
            .When(review => review.Track(JobStatus.NeedsHelp))
            .Then(review => Assert.Equal((false, true, "Needs help"), (review.ApproveCommand.CanExecute(null), review.RequestDiscardCommand.CanExecute(null), review.Heading)));

    [Fact]
    public void AJobHeldForInvalidChecksIsToldToCommitTheFixedFileAndResubmit() =>
        ViewModelScript.Given(Review())
            .When(review => review.Track(Held(HoldReason.InvalidChecks)))
            .ThenNotified(nameof(ReviewViewModel.Advice))
            .Then(review => Assert.Equal(
                ("Fix .avala/checks.json and commit it, then resubmit the job. Continuing it would check it against the old file again: a job keeps the rules commit it was prepared with.", true),
                (review.Advice, review.ResubmitCommand.CanExecute(null))));

    [Fact]
    public void OnlyATopLevelJobHeldForInvalidChecksIsOfferedAResubmission()
    {
        var offered = new[]
        {
            Held(HoldReason.Stalled),
            Held(HoldReason.InvalidChecks) with { Summary = job with { Status = JobStatus.NeedsHelp, Parent = JobId.New() } },
            Held(HoldReason.InvalidChecks) with { Summary = job with { Status = JobStatus.AwaitingReview } },
        }.Select(shown =>
        {
            var review = Review();
            review.Track(shown);

            return (review.Advice, review.ResubmitCommand.CanExecute(null));
        });

        Assert.All(offered, shown => Assert.Equal((string.Empty, false), shown));
    }

    [Fact]
    public async Task ResubmittingSubmitsTheSameJobAgainThenDiscardsTheHeldOne()
    {
        var held = Held(HoldReason.InvalidChecks) with
        {
            Summary = job with
            {
                Status = JobStatus.NeedsHelp,
                Connection = new Agents.Contracts.Connections.ConnectionName("claude-work"),
                Autonomy = Autonomy.Supervised,
                Model = new Agents.Contracts.Sessions.ModelChoice("opus", "high"),
            },
        };
        var review = Review();
        review.Track(held);
        var announced = new List<JobId>();
        review.Resubmitted += (_, resubmitted) => announced.Add(resubmitted);

        await review.ResubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            new JobRequest("/repo", "Rate-limit POST /login")
            {
                Connection = new Agents.Contracts.Connections.ConnectionName("claude-work"),
                Autonomy = Autonomy.Supervised,
                Model = new Agents.Contracts.Sessions.ModelChoice("opus", "high"),
            },
            Assert.Single(bench.Jobs.Requests));
        Assert.Equal(["submit Rate-limit POST /login", "discard"], bench.Jobs.Calls);
        Assert.Equal(("Resubmitted", "Resubmitted as a new job", true), (review.Heading, review.Outcome, review.IsClosed));
        Assert.Single(announced);
    }

    [Fact]
    public async Task AConnectionAvalaChoseIsLeftForAvalaToRouteAgainOnResubmission()
    {
        var chosen = new Agents.Contracts.Connections.ConnectionName("claude-spare");
        var review = Review();
        review.Track(Held(HoldReason.InvalidChecks) with
        {
            Summary = job with { Status = JobStatus.NeedsHelp, Connection = chosen },
            Choice = new ConnectionChoice(chosen, ChoiceReason.MostCapacity, [], DateTimeOffset.UnixEpoch),
        });

        await review.ResubmitCommand.ExecuteAsync(null);

        Assert.Equal(Option<Agents.Contracts.Connections.ConnectionName>.None, Assert.Single(bench.Jobs.Requests).Connection);
    }

    [Fact]
    public async Task ARefusedResubmissionSaysWhyInPlaceAndKeepsTheHeldJob()
    {
        bench.Jobs.Refusal = JobRejection.UnusableConnection;
        var review = Review();
        review.Track(Held(HoldReason.InvalidChecks));

        await review.ResubmitCommand.ExecuteAsync(null);

        Assert.Equal(["submit Rate-limit POST /login"], bench.Jobs.Calls);
        Assert.Equal(
            ("The job was not resubmitted. That connection cannot be used: its credential or the repository's jobs.json is not usable.", false, true),
            (review.Refusal, review.IsClosed, review.ResubmitCommand.CanExecute(null)));
    }

    public void Dispose() => bench.Dispose();

    private Workbench.Board.BoardJob Held(HoldReason reason) =>
        Bench.OnBoard(job with { Status = JobStatus.NeedsHelp }) with { Hold = reason };

    private static CheckEvidence Passing(string name) => new(name, name, CheckStatus.Passed, 0, TimeSpan.FromSeconds(1), string.Empty, string.Empty);

    private ReviewViewModel Review()
    {
        var review = new ReviewViewModel(job.Job, bench.Reader, bench.Desk, bench.Ui);
        review.Track(JobStatus.AwaitingReview);

        return review;
    }

    private RunEvidence Evidence(params VerificationReport[] reports) =>
        new(job.Job, new EvidenceSummary(reports.Length, Option<VerificationOutcome>.None, [], 0, 0, []), []) { Verifications = reports };

    private VerificationReport Report(int attempt, VerificationOutcome outcome, params CheckEvidence[] checks) =>
        new(job.Job, attempt, outcome, Option<FileOrigin>.None, checks, GateVerdict.Pass, DateTimeOffset.UnixEpoch);
}
