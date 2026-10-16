using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class CiEntryTests
{
    private static readonly PullRequestRef Pull = new(12, new Uri("https://github.com/acme/shop/pull/12"), "avala/job", "main");

    [Fact]
    public void ChecksStillRunningAddNoEntry() =>
        Assert.Empty(Transcript.Empty.WithCi(Watch("4f2a9c1", CheckStatus.Pending, CheckStatus.Failed), []).Entries);

    [Fact]
    public void AFailedHeadAddsOneEntryHoweverOftenItIsPolled()
    {
        var watch = Watch("4f2a9c1", CheckStatus.Passed, CheckStatus.Failed);

        var transcript = Transcript.Empty.WithCi(watch, []).WithCi(watch, []).WithCi(watch, []);

        var ci = Assert.IsType<CiEntry>(Assert.Single(transcript.Entries));
        Assert.Equal((12, "4f2a9c1", true, false), (ci.PullRequest, ci.Head, ci.Failed, ci.Woken));
    }

    [Fact]
    public void EachNewHeadAddsItsOwnEntry() =>
        Assert.Equal(
            [true, false],
            Transcript.Empty.WithCi(Watch("4f2a9c1", CheckStatus.Failed), []).WithCi(Watch("9b07d3e", CheckStatus.Passed), []).Entries.OfType<CiEntry>().Select(ci => ci.Failed));

    [Fact]
    public void AWakeUpForTheFailedHeadMarksItsEntryWoken()
    {
        var watch = Watch("4f2a9c1", CheckStatus.Failed);
        var woken = new WakeUpRecord(watch.Job, 12, WakeReason.ChecksFailed, "checks:4f2a9c1", "Fix it", DateTimeOffset.UnixEpoch, WakeOutcome.Woken);

        var transcript = Transcript.Empty.WithCi(watch, []).WithCi(watch, [woken]);

        Assert.True(Assert.IsType<CiEntry>(Assert.Single(transcript.Entries)).Woken);
    }

    private static PullRequestWatchState Watch(string head, params CheckStatus[] checks) =>
        new(JobId.New(), new ForgeName("github"), Pull, OnPullRequest.WatchOnly, 3)
        {
            Last = new PullRequestState(Pull, PullRequestLifecycle.Open, head, Mergeability.Mergeable)
            {
                Checks = [.. checks.Select((status, index) => new CheckRun($"check {index}", status, Option<Uri>.None, string.Empty))],
            },
        };
}
