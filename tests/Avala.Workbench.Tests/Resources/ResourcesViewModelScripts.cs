using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Resources;
using Avala.Workbench.Upkeep;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Resources;

public sealed class ResourcesTests : IDisposable
{
    private readonly FakeResources resources = new();
    private readonly FakeOrphans orphans = new();
    private readonly FakeHousekeeping worktrees = new();
    private readonly Leftovers leftovers = new();
    private readonly TestUiDispatcher ui = new();
    private readonly JobSummary job = Pages.Summary("Fix the failing test", JobStatus.Discarded);

    [Fact]
    public async Task EachAgentTreeIsAttributedToItsJobConnectionAndProviderAndOnlyTheLatestReportOfATreeCountsAsync()
    {
        var tree = new ProcessTreeId(Guid.NewGuid());
        resources.Latest = new ResourceSample(
            DateTimeOffset.UnixEpoch,
            [new TreeUsage(tree, "/worktrees/1", [new ProcessUsage(41, "dotnet", 64L * 1024 * 1024, TimeSpan.FromSeconds(1), [24001])], 0.5)
            {
                Job = job.Job,
                Connection = new ConnectionName("work"),
                Provider = "simulator",
            }],
            [],
            0);
        var left = Orphan(OrphanDisposal.LeftRunning);
        orphans.Reports.AddRange([left, left with { Disposal = OrphanDisposal.Killed }, Orphan(OrphanDisposal.LeftRunning)]);
        await leftovers.HandleAsync(new WorktreesReconciled(new WorktreeReconciliation(["/worktrees/stray"], []), false), CancellationToken.None);

        var state = Reader().Read();

        var shown = new AgentTreeViewModel(Assert.Single(state.Trees));
        Assert.Equal(("Fix the failing test", "work", "simulator", 1, "64 MB", "50%", "24001"), (shown.Job, shown.Connection, shown.Provider, shown.Processes, shown.Memory, shown.Cpu, shown.Ports));
        Assert.Equal([OrphanDisposal.Killed, OrphanDisposal.LeftRunning], state.Orphans.Select(report => report.Disposal).Order());
        Assert.Equal(2, state.Leftovers);
    }

    [Fact]
    public async Task ACleanedReconciliationLeavesNoStaleWorktreeAsync()
    {
        await leftovers.HandleAsync(new WorktreesReconciled(new WorktreeReconciliation(["/worktrees/stray"], []), false), CancellationToken.None);
        await leftovers.HandleAsync(new WorktreesReconciled(new WorktreeReconciliation(["/worktrees/stray"], []), true), CancellationToken.None);

        Assert.Empty(leftovers.Stale.Strays);
    }

    [Fact]
    public async Task AnOrphanLeftRunningIsReapedByItsJobAndNothingLeftIsShownAsAReasonAsync()
    {
        orphans.Reports.Add(Orphan(OrphanDisposal.LeftRunning));
        using var page = Page();
        page.Activate();
        await ui.UntilAsync(() => page.Orphans.Count == 1);
        var orphan = await ui.ReadAsync(() => page.Orphans[0]);

        await ui.InvokeAsync(() => page.ReapCommand.Execute(orphan), TestContext.Current.CancellationToken);
        await ui.UntilAsync(() => !page.ReapCommand.IsRunning);
        orphans.Reports.Clear();
        await ui.InvokeAsync(() => page.ReapCommand.Execute(orphan), TestContext.Current.CancellationToken);
        await ui.UntilAsync(() => !page.ReapCommand.IsRunning);

        Assert.Equal([job.Job, job.Job], orphans.Reaped);
        Assert.Equal((true, "Nothing of that job is left running."), (orphan.CanReap, await ui.ReadAsync(() => page.Error)));
    }

    [Fact]
    public async Task StaleWorktreesAreReconciledAndCleanedThroughHousekeepingAsync()
    {
        using var page = Page();

        await page.ReconcileCommand.ExecuteAsync(null);
        await page.CleanCommand.ExecuteAsync(null);

        Assert.Equal(["reconcile", "clean"], worktrees.Calls);
    }

    [Fact]
    public async Task TheIndicatorShowsTheMemoryInUseAndTheLeftoversAsync()
    {
        orphans.Reports.Add(Orphan(OrphanDisposal.LeftRunning));
        using var indicator = new ResourceIndicatorViewModel(Reader(), new LiveFeed(new Pulse(new JobBoard()), ui));

        indicator.Activate();
        await ui.UntilAsync(() => indicator.HasLeftovers);

        Assert.Equal(("512 MB", 1), await ui.ReadAsync(() => (indicator.Memory, indicator.Leftovers)));
    }

    public void Dispose() => ui.Dispose();

    private OrphanReport Orphan(OrphanDisposal disposal) =>
        new(new ProcessTreeId(Guid.NewGuid()), [new ProcessUsage(42, "server", 1024, TimeSpan.Zero, [24002])], disposal, [], DateTimeOffset.UnixEpoch) { Job = job.Job };

    private ResourceReader Reader() => new(resources, orphans, leftovers, Pages.Board(job));

    private ResourcesViewModel Page() =>
        new(Reader(), new Housekeeping(orphans, worktrees), new LiveFeed(new Pulse(new JobBoard()), ui));
}
