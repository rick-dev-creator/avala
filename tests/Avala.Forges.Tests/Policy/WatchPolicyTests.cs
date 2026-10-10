using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Forges.Tests.Policy;

public sealed class WatchPolicyTests
{
    private const string Head = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    private static readonly PullRequestRef Pull = new(7, new Uri("https://forge.invalid/octo/shop/pulls/7"), "avala/job", "main");

    private static readonly HashSet<string> Nothing = [];

    [Theory]
    [InlineData("Merged", "Merged")]
    [InlineData("Closed", "Closed")]
    public void AMergedOrClosedPullRequestEndsTheWatchWhateverElse(string lifecycle, string end)
    {
        var observed = Failing() with { Lifecycle = Enum.Parse<PullRequestLifecycle>(lifecycle) };

        var decision = WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), Nothing, observed, JobStatus.Approved);

        Assert.Equal((Verdict.End, Option<WatchEnd>.Some(Enum.Parse<WatchEnd>(end))), (decision.Verdict, decision.End));
    }

    [Fact]
    public void FailedChecksWithNonePendingWakeTheAgentOncePerHead()
    {
        var decision = WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), Nothing, Failing(), JobStatus.Approved);

        Assert.Equal((Verdict.Wake, new Trigger(WakeReason.ChecksFailed, $"checks:{Head}")), (decision.Verdict, Outcomes.Present(decision.Trigger)));
        Assert.Equal(Verdict.KeepWatching, WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), new HashSet<string> { $"checks:{Head}" }, Failing(), JobStatus.Approved).Verdict);
    }

    [Fact]
    public void AStillPendingCheckKeepsWatchingWithoutWaking()
    {
        var observed = Failing() with { Checks = [Check("tests", CheckStatus.Failed), Check("lint", CheckStatus.Pending)] };

        var decision = WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), Nothing, observed, JobStatus.Approved);

        Assert.Equal((Verdict.KeepWatching, false), (decision.Verdict, decision.Trigger.IsSome));
    }

    [Theory]
    [InlineData("WakeOnCi", "Wake", "Conflict")]
    [InlineData("WakeOnCiAndReviews", "Wake", "Conflict")]
    [InlineData("WatchOnly", "KeepWatching", "Conflict")]
    public void AConflictWakesTheAgentUnderEitherWakingPolicy(string policy, string verdict, string reason)
    {
        var observed = Passing() with { Mergeability = Mergeability.Conflicting };

        var decision = WatchPolicy.Decide(Watch(Enum.Parse<OnPullRequest>(policy)), Nothing, observed, JobStatus.Approved);

        Assert.Equal((Enum.Parse<Verdict>(verdict), Enum.Parse<WakeReason>(reason)), (decision.Verdict, Outcomes.Present(decision.Trigger).Reason));
    }

    [Theory]
    [InlineData("WakeOnCiAndReviews", "Wake")]
    [InlineData("WakeOnCi", "End")]
    [InlineData("WatchOnly", "End")]
    public void AReviewRequestingChangesWakesTheAgentOnlyWhenThePolicyWatchesReviews(string policy, string verdict)
    {
        var observed = Passing() with { Reviews = [new Review("80", "hubot", ReviewVerdict.ChangesRequested, "Rename it.", Head)] };

        var decision = WatchPolicy.Decide(Watch(Enum.Parse<OnPullRequest>(policy)), Nothing, observed, JobStatus.Approved);

        Assert.Equal(Enum.Parse<Verdict>(verdict), decision.Verdict);
    }

    [Fact]
    public void UnderWatchOnlyAFailureIsShownAndNeverWakes()
    {
        var decision = WatchPolicy.Decide(Watch(OnPullRequest.WatchOnly), Nothing, Failing(), JobStatus.Approved);

        Assert.Equal((Verdict.KeepWatching, WakeReason.ChecksFailed), (decision.Verdict, Outcomes.Present(decision.Trigger).Reason));
    }

    [Fact]
    public void SpentWakeUpsHoldTheWatchForAPersonOnce()
    {
        var spent = Watch(OnPullRequest.WakeOnCi) with { WakeUps = 2 };

        Assert.Equal(Verdict.HoldForPerson, WatchPolicy.Decide(spent, Nothing, Failing(), JobStatus.Approved).Verdict);
        Assert.Equal(Verdict.KeepWatching, WatchPolicy.Decide(spent with { Status = WatchStatus.NeedsPerson }, Nothing, Failing(), JobStatus.Approved).Verdict);
    }

    [Theory]
    [InlineData("Running", "WaitForJob")]
    [InlineData("Checking", "WaitForJob")]
    [InlineData("NeedsHelp", "WaitForJob")]
    [InlineData("AwaitingReview", "WaitForJob")]
    [InlineData("Failed", "End")]
    [InlineData("Discarded", "End")]
    public void AJobThatIsNotApprovedIsNeverWoken(string job, string verdict) =>
        Assert.Equal(Enum.Parse<Verdict>(verdict), WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), Nothing, Failing(), Enum.Parse<JobStatus>(job)).Verdict);

    [Theory]
    [InlineData("WakeOnCi", false, "End")]
    [InlineData("WatchOnly", false, "End")]
    [InlineData("WakeOnCiAndReviews", false, "KeepWatching")]
    [InlineData("WakeOnCiAndReviews", true, "End")]
    public void PassedChecksOnAMergeablePullRequestEndTheWatchGreenAndUnderReviewsOnlyOnceApproved(string policy, bool approved, string verdict)
    {
        var observed = Passing() with { Reviews = approved ? [new Review("82", "lisa", ReviewVerdict.Approved, "Nice.", Head)] : [] };

        var decision = WatchPolicy.Decide(Watch(Enum.Parse<OnPullRequest>(policy)), Nothing, observed, JobStatus.Approved);

        Assert.Equal(Enum.Parse<Verdict>(verdict), decision.Verdict);
        Assert.Equal(decision.Verdict == Verdict.End, decision.End == Option<WatchEnd>.Some(WatchEnd.Green));
    }

    [Fact]
    public void AnUnknownMergeabilityIsNotGreenYet() =>
        Assert.Equal(Verdict.KeepWatching, WatchPolicy.Decide(Watch(OnPullRequest.WakeOnCi), Nothing, Passing() with { Mergeability = Mergeability.Unknown }, JobStatus.Approved).Verdict);

    [Theory]
    [InlineData(60, 0, 60)]
    [InlineData(60, 1, 120)]
    [InlineData(60, 2, 240)]
    [InlineData(60, 4, 960)]
    [InlineData(60, 9, 960)]
    [InlineData(300, 4, 1800)]
    [InlineData(3600, 3, 3600)]
    public void BackoffDoublesThePollPerFailureUpToSixteenTimesAndHalfAnHour(int poll, int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Backoff.After(TimeSpan.FromSeconds(poll), failures));

    [Fact]
    public void TheFeedbackOfFailedChecksNamesEachFailureWithItsSummaryAndLink()
    {
        var feedback = WakeFeedback.For(new Trigger(WakeReason.ChecksFailed, $"checks:{Head}"), Failing(), "origin");

        Assert.Contains("The checks of pull request #7 failed on 6dcb09b:", feedback, StringComparison.Ordinal);
        Assert.Contains("- tests: 1 test failed (https://ci.example/4)", feedback, StringComparison.Ordinal);
        Assert.EndsWith(WakeFeedback.Closing, feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFeedbackOfAConflictTellsTheAgentToMergeTheFetchedBase() =>
        Assert.Contains(
            "Avala fetched origin/main: merge it into your branch and resolve the conflicts.",
            WakeFeedback.For(new Trigger(WakeReason.Conflict, $"conflict:{Head}"), Passing(), "origin"),
            StringComparison.Ordinal);

    [Fact]
    public void TheFeedbackOfAReviewQuotesItAndItsCommentsByFileAndLine()
    {
        var observed = Passing() with
        {
            Reviews = [new Review("80", "hubot", ReviewVerdict.ChangesRequested, "Rename it.", Head) { Comments = [new ReviewComment("GREETING.md", 1, "Here.")] }],
        };

        var feedback = WakeFeedback.For(new Trigger(WakeReason.ChangesRequested, "review:80"), observed, "origin");

        Assert.Contains("hubot: Rename it.\n- GREETING.md:1: Here.", feedback, StringComparison.Ordinal);
    }

    private static PullRequestWatchState Watch(OnPullRequest policy) => new(JobId.New(), new ForgeName("github"), Pull, policy, 2);

    private static CheckRun Check(string name, CheckStatus status) =>
        new(name, status, status == CheckStatus.Failed ? new Uri("https://ci.example/4") : Option<Uri>.None, status == CheckStatus.Failed ? "1 test failed" : string.Empty);

    private static PullRequestState Passing() =>
        new(Pull, PullRequestLifecycle.Open, Head, Mergeability.Mergeable) { Checks = [Check("build", CheckStatus.Passed), Check("docs", CheckStatus.Skipped)] };

    private static PullRequestState Failing() =>
        Passing() with { Checks = [Check("build", CheckStatus.Passed), Check("tests", CheckStatus.Failed)] };
}
