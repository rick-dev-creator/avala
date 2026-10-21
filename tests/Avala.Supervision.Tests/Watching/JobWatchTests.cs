using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Tests.Watching;

public sealed class JobWatchTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId Turn = TurnId.New();

    [Fact]
    public void APermissionAskedInATurnTheAgentWokeBeforeItsJobRunsIsStillHumanTimeOnceTheJobRuns()
    {
        var watch = new JobWatch(JobId.New()).Joined(Session)
            .Saw(new TurnStarted(Session, Turn) { Woken = true }, Now)
            .Saw(new PermissionRequested(Session, Turn, new ItemId("read"), "Read the results", ItemKind.FileEdit, "results.txt"), Now)
            .Progressed(JobStatus.Running, Now);

        Assert.True(watch.SilenceAt(Now.AddHours(2), JobWatch.DefaultSilence).IsNone);
    }

    [Fact]
    public void AJobContinuedInANewSessionIsWatchedAfreshThere()
    {
        var watch = new JobWatch(JobId.New()).Joined(Session)
            .Saw(new TurnStarted(Session, Turn), Now)
            .Saw(new PermissionRequested(Session, Turn, new ItemId("read"), "Read the results", ItemKind.FileEdit, "results.txt"), Now)
            .Progressed(JobStatus.NeedsHelp, Now)
            .Joined(SessionId.New())
            .Progressed(JobStatus.Running, Now);

        Assert.True(watch.SilenceAt(Now.AddHours(2), JobWatch.DefaultSilence).IsSome);
    }
}
