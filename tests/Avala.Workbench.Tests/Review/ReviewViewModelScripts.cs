using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Review;
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
            Verifications = [Report(1, VerificationOutcome.Failed, failing), Report(2, VerificationOutcome.Passed)],
            AllowedByRules = 2,
        };
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);
        bench.Changes.Files = [new FileChange("calculator.txt", ChangeKind.Added, 1, 0)];
        var review = Review();

        await review.Request(0)(Cancellation);

        Assert.Equal("Verified on attempt 2 of 2", review.Verdict);
        var exception = Assert.Single(review.Exceptions);
        Assert.Equal(("Attempt 1 failed calculator (exit 1)", "add(2, 2) = 5"), (exception.Title, exception.Detail));
        Assert.Equal("2 other decisions were allowed by rules · USD 0.25 · 1,500 tokens", review.Quiet);
        Assert.Equal(("1 file changed", "calculator.txt", "+1 -0"), (review.Changes, review.Files[0].Path, review.Files[0].Counts));
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
        Assert.Equal(["-add(2, 2) = 5", "+add(2, 2) = 4"], Assert.Single(file.Hunks).Lines);
    }

    [Fact]
    public async Task AnApprovalRefusedForAConflictShowsTheConflictingFilesInPlace()
    {
        bench.Jobs.Refusal = JobRejection.MergeConflict;
        bench.Changes.Conflicts = ["calculator.txt"];
        var review = Review();

        await review.ApproveCommand.ExecuteAsync(null);

        Assert.Equal(("The work conflicts with the base branch in calculator.txt.", "calculator.txt"), (review.Outcome, string.Join(",", review.Conflicts)));
    }

    [Fact]
    public async Task AnApprovedJobShowsHowItWasDelivered()
    {
        var review = Review();

        await review.ApproveCommand.ExecuteAsync(null);

        Assert.Equal("Approved: the branch avala/fix-the-test is ready", review.Outcome);
    }

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
    }

    [Fact]
    public async Task DiscardingTakesARequestAndAConfirmation()
    {
        var review = Review();
        var direct = review.ConfirmDiscardCommand.CanExecute(null);

        review.RequestDiscardCommand.Execute(null);
        review.CancelDiscardCommand.Execute(null);
        var cancelled = bench.Jobs.Calls.Count;
        review.RequestDiscardCommand.Execute(null);
        await review.ConfirmDiscardCommand.ExecuteAsync(null);

        Assert.Equal((false, 0, false), (direct, cancelled, review.ConfirmingDiscard));
        Assert.Equal(("Discarded", "discard"), (review.Outcome, string.Join(",", bench.Jobs.Calls)));
    }

    [Fact]
    public async Task AJobWithoutEvidenceOrWorktreeSaysSoAndIsLoaded()
    {
        var bare = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.NeedsHelp).Summary;
        var review = new ReviewViewModel(bare.Job, bench.Reader, bench.Desk, bench.Ui);

        await ViewModelScript.Given(review).WhenPresentedAsync(shown => _ = shown.Request(0)(Cancellation), Cancellation);

        Assert.Equal(("No evidence for this job", "The job has no worktree", false, true), (review.Verdict, review.Changes, review.HasExceptions, review.IsLoaded));
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

        Assert.Equal(("The base branch's checkout has uncommitted changes. Commit or stash them, then approve again.", 0), (review.Outcome, review.Conflicts.Count));
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
            .Then(review => Assert.Equal((false, true), (review.ApproveCommand.CanExecute(null), review.RequestDiscardCommand.CanExecute(null))));

    public void Dispose() => bench.Dispose();

    private ReviewViewModel Review()
    {
        var review = new ReviewViewModel(job.Job, bench.Reader, bench.Desk, bench.Ui);
        review.Track(JobStatus.AwaitingReview);

        return review;
    }

    private VerificationReport Report(int attempt, VerificationOutcome outcome, params CheckEvidence[] checks) =>
        new(job.Job, attempt, outcome, Option<FileOrigin>.None, checks, GateVerdict.Pass, DateTimeOffset.UnixEpoch);
}
