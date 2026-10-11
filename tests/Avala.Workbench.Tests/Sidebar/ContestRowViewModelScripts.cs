using System.Collections.Immutable;
using Avala.Components.Status;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class ContestRowViewModelScripts
{
    private readonly FakeCatalog catalog = new();

    [Fact]
    public void TogglingShowsTheCandidatesInTheOrderTheyEnteredAndHidesThemAgain()
    {
        var (first, second) = (Job("First"), Job("Second"));
        var contest = FakeContests.Contest(ContestStatus.Running, first.Job, second.Job);
        var row = new ContestRowViewModel(contest);
        row.Update(contest, ImmutableDictionary<JobId, BoardJob>.Empty.Add(second.Job, second));
        row.Update(contest, ImmutableDictionary<JobId, BoardJob>.Empty.Add(first.Job, first).Add(second.Job, second));

        ViewModelScript.Given(row)
            .When(shown => shown.ToggleCommand.Execute(null))
            .Then(shown => Assert.Equal([1, 2], shown.Children.Select(candidate => candidate.Ordinal)));
        row.ToggleCommand.Execute(null);
        Assert.Equal((false, 0), (row.IsExpanded, row.Children.Count));
    }

    [Fact]
    public void HighlightingSelectsTheRowOrOneOfItsCandidatesAlone()
    {
        var (first, second) = (Job("First"), Job("Second"));
        var contest = FakeContests.Contest(ContestStatus.Running, first.Job, second.Job);
        var row = new ContestRowViewModel(contest);
        row.Update(contest, ImmutableDictionary<JobId, BoardJob>.Empty.Add(first.Job, first).Add(second.Job, second));

        row.Highlight(row.Candidates[1]);

        Assert.Equal((false, false, true), (row.IsSelected, row.Candidates[0].IsSelected, row.Candidates[1].IsSelected));
    }

    [Theory]
    [InlineData(ContestStatus.Running, nameof(JobGroup.Running), StatusKind.Working)]
    [InlineData(ContestStatus.Judging, nameof(JobGroup.Running), StatusKind.Checking)]
    [InlineData(ContestStatus.Decided, nameof(JobGroup.ReadyForReview), StatusKind.ReadyForReview)]
    [InlineData(ContestStatus.Undecided, nameof(JobGroup.NeedsYou), StatusKind.NeedsYou)]
    [InlineData(ContestStatus.Closed, nameof(JobGroup.Done), StatusKind.Done)]
    [InlineData(ContestStatus.Cancelled, nameof(JobGroup.Done), StatusKind.Failed)]
    public void TheRowsGroupAndDotFollowWhatTheContestNeeds(ContestStatus status, string group, StatusKind dot)
    {
        var contest = FakeContests.Contest(status, JobId.New(), JobId.New());
        var row = new ContestRowViewModel(contest);

        row.Update(contest, ImmutableDictionary<JobId, BoardJob>.Empty);

        Assert.Equal((group, dot, "2 candidates · 0 passed"), (row.Group.ToString(), row.Dot.Kind, row.Fact));
    }

    [Fact]
    public void TheRowLeadsWithTheWinnerOnceDecidedAndWithThePickedCandidateOnceClosed()
    {
        var (first, second) = (JobId.New(), JobId.New());
        var decided = FakeContests.Contest(ContestStatus.Decided, first, second) with { Verdict = new ContestVerdict([], second, DateTimeOffset.UnixEpoch) };
        var row = new ContestRowViewModel(decided);
        var leading = row.Job;

        row.Update(decided, ImmutableDictionary<JobId, BoardJob>.Empty);
        var winner = row.Job;
        row.Update(decided with { Status = ContestStatus.Closed, Picked = first }, ImmutableDictionary<JobId, BoardJob>.Empty);

        Assert.Equal((first, second, first), (leading, winner, row.Job));
    }

    private BoardJob Job(string instruction) => new(catalog.Add(instruction, JobStatus.Running).Summary, Transcript.Empty);
}
