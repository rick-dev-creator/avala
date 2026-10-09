using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Settings;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Resources.Tests.Reaping;

public sealed class OrphanReaperTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task AProcessAliveAfterItsSessionStoppedIsReportedWithItsJobAndKilledByDefaultAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var (session, tree) = await resources.OpenAsync(job, Resourced.Home("one"), members: Server);
        resources.Listening.Listeners.Add(new Listener(24_000, 41));

        await resources.Tracker.HandleAsync(new SessionStopped(session), Cancellation);

        var report = Assert.Single(resources.Bus.Published.OfType<OrphansFound>()).Report;
        Assert.Equal(
            (tree.Id, Option<SessionId>.Some(session), Option<JobId>.Some(job), OrphanDisposal.Killed, 41, "dotnet", 24_000),
            (report.Tree, report.Session, report.Job, report.Disposal, Assert.Single(report.Processes).Id, report.Processes[0].Name, Assert.Single(report.Processes[0].Ports)));
        Assert.Empty(report.Survivors);
        Assert.Equal([tree.Id], resources.Trees.Closed);
        Assert.Equal([report], resources.Reaper.OfJob(job));
    }

    [Fact]
    public async Task UnderTheReportPolicyAnOrphanIsLeftRunningUntilACommandReapsItAsync()
    {
        await using var resources = new Resourced(ResourceSettingsParser.Defaults with { Orphans = OrphanPolicy.Report });
        var job = JobId.New();
        var (session, tree) = await resources.OpenAsync(job, Resourced.Home("one"), members: Server);

        await resources.Tracker.HandleAsync(new SessionEnded(session, SessionEnding.Crashed), Cancellation);
        await resources.Tracker.HandleAsync(new SessionStopped(session), Cancellation);
        var found = Assert.Single(resources.Bus.Published.OfType<OrphansFound>()).Report;
        Assert.Empty(resources.Trees.Closed);

        var reaped = Assert.Single(Outcomes.Succeeds(await resources.Reaper.ReapAsync(job, Cancellation)));

        Assert.Equal(OrphanDisposal.LeftRunning, found.Disposal);
        Assert.Equal((OrphanDisposal.Killed, tree.Id), (reaped.Disposal, reaped.Tree));
        Assert.Equal(reaped, Assert.Single(resources.Bus.Published.OfType<OrphansReaped>()).Report);
        Assert.Equal([tree.Id], resources.Trees.Closed);
        Assert.Equal([found, reaped], resources.Reaper.Audit());
        Assert.Equal(ResourceError.NothingToReap, Outcomes.FailsWith(await resources.Reaper.ReapAsync(job, Cancellation)));
    }

    [Fact]
    public async Task AProcessThatSurvivesTheKillIsReportedAsASurvivorAsync()
    {
        await using var resources = new Resourced();
        var (session, tree) = await resources.OpenAsync(JobId.New(), Resourced.Home("one"), members: Server);
        tree.Unkillable.AddRange(Server);

        await resources.Tracker.HandleAsync(new SessionStopped(session), Cancellation);

        Assert.Equal([41], Assert.Single(resources.Bus.Published.OfType<OrphansFound>()).Report.Survivors);
    }

    [Fact]
    public async Task ASessionThatLeavesNothingBehindIsNotReportedAndItsTreeIsClosedAsync()
    {
        await using var resources = new Resourced();
        var (session, tree) = await resources.OpenAsync(JobId.New(), Resourced.Home("one"));

        await resources.Tracker.HandleAsync(new SessionStopped(session), Cancellation);

        Assert.Empty(resources.Bus.Published.OfType<OrphansFound>());
        Assert.Equal([tree.Id], resources.Trees.Closed);
    }

    private static TreeProcess[] Server => [new(41, "dotnet", 2_048, TimeSpan.FromSeconds(1))];
}
