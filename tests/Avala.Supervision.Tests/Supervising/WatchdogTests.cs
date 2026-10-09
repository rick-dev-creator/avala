using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using static Avala.Supervision.Tests.Supervising.Supervised;

namespace Avala.Supervision.Tests.Supervising;

public sealed class WatchdogTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ARunningJobSilentForTheWholeWindowIsHeldAsStalledWithTheSilenceMeasuredAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();

        await supervised.AdvanceAsync(Window);

        Assert.Equal([(supervised.Job, HoldReason.Stalled)], supervised.Jobs.Holds);
        var intervention = Assert.Single(supervised.Interventions);
        Assert.Equal(
            (HoldReason.Stalled, new SilenceMeasure(Window, Window), supervised.Clock.GetUtcNow()),
            (intervention.Hold.Reason, intervention.Silence, intervention.At));
        Assert.Equal(new SupervisorIntervened(intervention), supervised.Bus.Published[^1]);
    }

    [Fact]
    public async Task ActivityRestartsTheWindowFromTheLastEventAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();
        await supervised.AdvanceAsync(Window - Minute);
        await supervised.SeeAsync(new ItemStarted(supervised.Session, supervised.Turn, new ItemId("build"), ItemKind.Command, "Build"));

        await supervised.AdvanceAsync(Minute);
        Assert.Empty(supervised.Jobs.Holds);

        await supervised.AdvanceAsync(Window - Minute);
        Assert.Equal(new SilenceMeasure(Window, Window), Assert.Single(supervised.Interventions).Silence);
    }

    [Fact]
    public async Task WaitingForAHumanToAnswerAPermissionIsNeverSilenceAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();
        var item = new ItemId("migrate");
        await supervised.SeeAsync(new PermissionRequested(supervised.Session, supervised.Turn, item, "Run", ItemKind.Command, "dotnet ef"));

        await supervised.AdvanceAsync(Window * 10);
        Assert.Empty(supervised.Jobs.Holds);

        await supervised.SeeAsync(new PermissionResolved(supervised.Session, supervised.Turn, item, PermissionAnswer.Allow));
        await supervised.AdvanceAsync(Window);
        Assert.Single(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task AJobThatIsNotRunningIsNeverHeldForSilenceAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();

        await supervised.ProgressAsync(JobStatus.Checking);
        await supervised.AdvanceAsync(Window * 2);

        Assert.Empty(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task OnlyActivityOfTheJobsCurrentSessionKeepsItAwakeAsync()
    {
        await using var supervised = new Supervised();
        var replaced = SessionId.New();
        await supervised.JoinAsync(replaced);
        await supervised.RunningAsync();
        await supervised.AdvanceAsync(Window - Minute);

        await supervised.SeeAsync(new TurnStarted(replaced, TurnId.New()));
        await supervised.AdvanceAsync(Minute);

        Assert.Single(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task AHoldJobsRejectsIsNotRecordedAsAnInterventionAsync()
    {
        await using var supervised = new Supervised(JobRejection.NotRunning);
        await supervised.RunningAsync();

        await supervised.AdvanceAsync(Window);

        Assert.Single(supervised.Jobs.Holds);
        Assert.Empty(supervised.Interventions);
        Assert.DoesNotContain(supervised.Bus.Published, published => published is SupervisorIntervened);
    }
}
