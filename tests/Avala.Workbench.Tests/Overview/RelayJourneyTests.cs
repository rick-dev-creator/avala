using Avala.Agents.Contracts.Connections;
using Avala.Components.Graphs;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Overview;

namespace Avala.Workbench.Tests.Overview;

public sealed class RelayJourneyTests
{
    private static readonly DateTimeOffset One = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    private static readonly ConnectionName Work = new("claude-work");

    private static readonly ConnectionName Team = new("claude-team");

    private static readonly ConnectionName Max = new("claude-max");

    private static readonly JobSummary Job = Pages.Summary("Migrate the billing service", JobStatus.Running, "claude-max") with { Submitted = One };

    [Fact]
    public void AJobHandedOffTwiceRunsThreeLegsInOrderAndIsRunningOnTheLast()
    {
        var journey = RelayJourneys.Of(Job, [Handoff(Work, Team, 2), Handoff(Team, Max, 4)], [], Option<ResetWait>.None, One.AddHours(5));

        Assert.Equal([Work, Team, Max], journey.Connections);
        Assert.Equal(
            [(Work, RelayLegKind.Ran, One, One.AddHours(2)), (Team, RelayLegKind.Ran, One.AddHours(2), One.AddHours(4)), (Max, RelayLegKind.Running, One.AddHours(4), One.AddHours(5))],
            journey.Legs.Select(leg => (leg.Connection, leg.Kind, leg.From, leg.To)));
        Assert.Equal(2, journey.Handoffs);
    }

    [Fact]
    public void AJobWaitingForAResetEndsWithAWaitingLegOnTheConnectionThatResetsFirst()
    {
        var waiting = Job with { Status = JobStatus.NeedsHelp };
        var wait = new ResetWait(Job.Job, Work, "5h", One.AddHours(6), One.AddHours(4));

        var journey = RelayJourneys.Of(waiting, [Handoff(Work, Team, 2)], [], wait, One.AddHours(5));

        Assert.Equal((Work, RelayLegKind.Waiting, One.AddHours(4)), (journey.Legs[^1].Connection, journey.Legs[^1].Kind, journey.Legs[^1].From));
        Assert.Equal(Option<DateTimeOffset>.Some(One.AddHours(6)), journey.Legs[^1].ResumesAt);
        Assert.Equal([Work, Team], journey.Connections);
    }

    [Fact]
    public void AWaitingMarkAndTheWaitItRecordsAreOneLeg()
    {
        var waiting = Job with { Status = JobStatus.NeedsHelp };
        var wait = new ResetWait(Job.Job, Work, "5h", One.AddHours(6), One.AddHours(4));
        RelayMark[] marks = [new(Job.Job, RelayMarkKind.Waiting, Work, One.AddHours(4)) { Until = One.AddHours(6) }];

        var journey = RelayJourneys.Of(waiting, [Handoff(Work, Team, 2)], marks, wait, One.AddHours(5));

        Assert.Single(journey.Legs, leg => leg.Kind == RelayLegKind.Waiting);
    }

    [Fact]
    public void AJobResumedAtItsResetRunsAgainOnThatConnectionAfterItsWait()
    {
        RelayMark[] marks =
        [
            new(Job.Job, RelayMarkKind.Waiting, Work, One.AddHours(4)),
            new(Job.Job, RelayMarkKind.Compacting, Work, One.AddHours(6)),
            new(Job.Job, RelayMarkKind.Resumed, Work, One.AddHours(6)),
        ];

        var journey = RelayJourneys.Of(Job, [Handoff(Work, Team, 2)], marks, Option<ResetWait>.None, One.AddHours(7));

        Assert.Equal([RelayLegKind.Ran, RelayLegKind.Ran, RelayLegKind.Waiting, RelayLegKind.Running], journey.Legs.Select(leg => leg.Kind));
        Assert.Equal([Work, Team, Work, Work], journey.Legs.Select(leg => leg.Connection));
        Assert.True(journey.Legs[2].Compacting);
    }

    [Fact]
    public void AWaitThatAPersonEndedByContinuingShowsTheJobRunningAgain()
    {
        RelayMark[] marks = [new(Job.Job, RelayMarkKind.Waiting, Work, One.AddHours(4))];

        var journey = RelayJourneys.Of(Job, [Handoff(Work, Team, 2)], marks, Option<ResetWait>.None, One.AddHours(5));

        Assert.Equal(RelayLegKind.Running, journey.Legs[^1].Kind);
    }

    [Fact]
    public void AJobThatNeverNearedALimitHasNoJourney()
    {
        Assert.True(RelayJourneys.Of(Job, [], [], Option<ResetWait>.None, One.AddHours(1)).IsEmpty);
    }

    [Fact]
    public void ALegEndsWithTheWindDownOrCapThatClosedItAndTheCourseMarksIt()
    {
        RelayMark[] marks =
        [
            new(Job.Job, RelayMarkKind.WindingDown, Work, One.AddHours(2).AddMinutes(-5)) { Why = new LimitReason(Work, "5h", 0.91, 0.9) },
            new(Job.Job, RelayMarkKind.Capped, Team, One.AddHours(4).AddMinutes(-1)) { Why = new LimitReason(Team, "5h", 0.97, 0.9) },
        ];

        var journey = RelayJourneys.Of(Job, [Handoff(Work, Team, 2), Handoff(Team, Max, 4)], marks, Option<ResetWait>.None, One.AddHours(5));

        Assert.Equal([RelayMarkKind.WindingDown, RelayMarkKind.Capped], journey.Legs.Take(2).Select(leg => leg.Ending.Match(mark => mark.Kind, () => RelayMarkKind.Resumed)));
        Assert.Equal(
            [RelayStopKind.WindingDown, RelayStopKind.Capped, RelayStopKind.HandedOff, RelayStopKind.HandedOff],
            journey.Course().Stops.Select(stop => stop.Kind));
    }

    [Fact]
    public void TheCourseRunsFromStartToEndWithEveryLegOnItsConnectionsLane()
    {
        var journey = RelayJourneys.Of(Job, [Handoff(Work, Team, 1), Handoff(Team, Max, 4)], [], Option<ResetWait>.None, One.AddHours(5));

        var course = journey.Course();

        Assert.Equal(3, course.Lanes);
        Assert.Equal([0, 1, 2], course.Stretches.Select(stretch => stretch.Lane));
        Assert.Equal((0d, 1d), (course.Stretches[0].From, course.Stretches[^1].To));
        Assert.All(course.Stretches.Zip(course.Stretches.Skip(1)), pair => Assert.Equal(pair.First.To, pair.Second.From));
        Assert.True(course.Stretches[0].To - course.Stretches[0].From < course.Stretches[1].To - course.Stretches[1].From, "a shorter leg should take less of the course");
        Assert.True(course.IsLive);
    }

    private static HandoffRecord Handoff(ConnectionName from, ConnectionName to, int hours) =>
        new(Job.Job, hours, from, to, new LimitReason(from, "5h", 0.92, 0.9), One.AddHours(hours));
}
