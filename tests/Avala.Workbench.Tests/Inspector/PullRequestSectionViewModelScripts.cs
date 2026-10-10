using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;
using Avala.Workbench.Linking;

namespace Avala.Workbench.Tests.Inspector;

public sealed class PullRequestSectionViewModelScripts : IDisposable
{
    private static readonly PullRequestRef Pull = new(7, new Uri("https://github.com/octo/shop/pull/7"), "avala/fix-the-test", "main");

    private readonly Bench bench = new();

    [Fact]
    public async Task AWatchedPullRequestShowsItsChecksReviewsConflictsWatchStateAndWakeUpsAsync()
    {
        var job = Watched(WatchStatus.WaitingForJob, Mergeability.Conflicting);
        using var section = new PullRequestSectionViewModel(bench.Inspected(), FakeLinks.Opening, bench.Forge);

        await section.FocusAsync(job, bench);

        Assert.True(section.HasPullRequest);
        Assert.Equal(("Pull request #7 on github", "https://github.com/octo/shop/pull/7"), (section.Title, section.Link));
        Assert.Equal("The agent works on wake-up 1 of 3", section.Status);
        Assert.Equal("1 passed · 1 failed · 0 pending", section.Checks);
        Assert.Equal(["passed · build", "failed · tests: 1 test failed"], section.CheckLines);
        Assert.Equal("Conflicts with its base", section.Mergeability);
        Assert.Equal(["hubot requested changes"], section.Reviews);
        Assert.Equal(["Woke the agent: checks failed, resuming its conversation"], section.WakeUps);
    }

    [Fact]
    public async Task AJobWithoutAPullRequestSaysSoAndOffersNoCommandAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Approved);
        bench.Publish(Bench.OnBoard(job));
        using var section = new PullRequestSectionViewModel(bench.Inspected(), FakeLinks.Opening, bench.Forge);

        await section.FocusAsync(job.Job, bench);

        Assert.Equal((false, "none", "No pull request was opened for this job."), (section.HasPullRequest, section.Fact, section.Title));
        Assert.False(section.OpenLinkCommand.CanExecute(null));
        Assert.False(section.CheckNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningTheLinkGoesThroughTheLinkOpenerAndCheckingNowPollsTheForgeAsync()
    {
        var job = Watched(WatchStatus.Watching, Mergeability.Mergeable);
        var opener = new FakeLinks();
        using var section = new PullRequestSectionViewModel(bench.Inspected(), new Links(opener), bench.Forge);
        await section.FocusAsync(job, bench);

        await section.OpenLinkCommand.ExecuteAsync(null);
        await section.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal([Pull.Link], opener.Opened);
        Assert.Equal([job], bench.PullRequests.Refreshed);
        Assert.Equal(string.Empty, section.Notice);
    }

    public void Dispose() => bench.Dispose();

    private JobId Watched(WatchStatus status, Mergeability mergeability)
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Approved);
        var observed = new PullRequestState(Pull, PullRequestLifecycle.Open, "6dcb09b", mergeability)
        {
            Checks = [new CheckRun("build", CheckStatus.Passed, Option<Uri>.None, string.Empty), new CheckRun("tests", CheckStatus.Failed, Option<Uri>.None, "1 test failed")],
            Reviews = [new Forges.Contracts.Review("80", "hubot", ReviewVerdict.ChangesRequested, "Rename it.", "6dcb09b")],
        };
        var state = new PullRequestWatchState(job.Job, new ForgeName("github"), Pull, OnPullRequest.WakeOnCi, 3) { Status = status, WakeUps = 1, Last = observed };
        var wakeUp = new WakeUpRecord(job.Job, 7, WakeReason.ChecksFailed, "checks:6dcb09b", "Fix it.", DateTimeOffset.UnixEpoch, WakeOutcome.Woken) { Conversation = ContinuedIn.ResumedConversation };
        bench.PullRequests.Watches[job.Job] = state;
        bench.Publish(Bench.OnBoard(job) with { PullRequest = state, WakeUps = [wakeUp] });

        return job.Job;
    }
}
