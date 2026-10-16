using System.Collections.Immutable;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Inspector;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Inspector;

public sealed class ChecksRunningViewModelScripts : IDisposable
{
    private static readonly PullRequestRef Pull = new(7, new Uri("https://github.com/octo/shop/pull/7"), "avala/fix-the-test", "main");

    private readonly TestUiDispatcher ui = new();
    private readonly JobBoard board = new();
    private readonly JobSummary job = Pages.Summary("Fix the flaky test", JobStatus.AwaitingReview, "claude-work");

    [Fact]
    public async Task TheHeaderSaysChecksAreRunningWhileAnyIsPendingAsync()
    {
        Shows(CheckStatus.Passed, CheckStatus.Pending, CheckStatus.Pending);
        using var chip = Chip();

        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Text);

        Assert.Equal("Checks running · 1 of 3", await ui.ReadAsync(() => chip.Text));
        Assert.Equal(["passed · check 0", "pending · check 1", "pending · check 2"], await ui.ReadAsync(() => chip.Lines));
    }

    [Fact]
    public async Task TheHeaderShowsNothingOnceEveryCheckEndedAsync()
    {
        Shows(CheckStatus.Pending);
        using var chip = Chip();
        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Text);

        Shows(CheckStatus.Failed);

        await ui.PresentedAsync(chip, () => !chip.IsShown, () => chip.Text);
        Assert.False(await ui.ReadAsync(() => chip.IsShown));
    }

    public void Dispose() => ui.Dispose();

    private void Shows(params CheckStatus[] checks)
    {
        var watch = new PullRequestWatchState(job.Job, new ForgeName("github"), Pull, OnPullRequest.WatchOnly, 3)
        {
            Last = new PullRequestState(Pull, PullRequestLifecycle.Open, "4f2a9c1", Mergeability.Mergeable)
            {
                Checks = [.. checks.Select((status, index) => new CheckRun($"check {index}", status, Option<Uri>.None, string.Empty))],
            },
        };
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(job.Job, new BoardJob(job, Transcript.Empty) { PullRequest = watch }));
    }

    private ChecksRunningViewModel Chip()
    {
        var chip = new ChecksRunningViewModel(board, new LiveFeed(new Pulse(board), ui));
        chip.Activate();
        chip.OnRegionContextChanged(job.Job);

        return chip;
    }
}
