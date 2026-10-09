using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Resources;
using Avala.Workbench.Upkeep;

namespace Avala.Workbench.Tests.Resources;

public sealed class OrphanViewModelScripts
{
    [Fact]
    public void AnOrphanLeftRunningListsItsProcessesAndCanBeReaped() =>
        ViewModelScript.Given(new OrphanViewModel(Report(OrphanDisposal.LeftRunning, [])))
            .Then(orphan =>
            {
                Assert.Equal(["node (48213)"], orphan.Processes);
                Assert.Equal(("left running", true, true), (orphan.Disposal, orphan.IsLeftRunning, orphan.CanReap));
            });

    [Fact]
    public void AKilledOrphanSaysHowManyProcessesSurvived() =>
        ViewModelScript.Given(new OrphanViewModel(Report(OrphanDisposal.Killed, [48213])))
            .Then(orphan => Assert.Equal(("killed, 1 survived", false), (orphan.Disposal, orphan.CanReap)));

    private static OrphanReport Report(OrphanDisposal disposal, IReadOnlyList<int> survivors) =>
        new(new ProcessTreeId(Guid.NewGuid()), [new ProcessUsage(48213, "node", 1024, TimeSpan.Zero, [])], disposal, survivors, DateTimeOffset.UnixEpoch) { Job = JobId.New() };
}

public sealed class StaleWorktreeViewModelScripts
{
    [Fact]
    public void AStaleWorktreeSaysWhereItIsAndWhy() =>
        ViewModelScript.Given(new StaleWorktreeViewModel("/worktrees/old-checkout-spike", "not known to any job"))
            .Then(stale => Assert.Equal(("/worktrees/old-checkout-spike", "not known to any job"), (stale.Path, stale.Reason)));
}

public sealed class AgentTreeViewModelScripts
{
    [Fact]
    public void ATreeWithoutAJobSaysSo() =>
        ViewModelScript.Given(new AgentTreeViewModel(new TreeState(new TreeUsage(new ProcessTreeId(Guid.NewGuid()), "/worktrees/1", [], 0), Option<BoardJob>.None)))
            .Then(tree => Assert.Equal(("no job", string.Empty, string.Empty, 0, "0 MB"), (tree.Job, tree.Connection, tree.Provider, tree.Processes, tree.Memory)));
}

public sealed class ResourceIndicatorViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task TheIndicatorShowsTheMemoryInUseAndTheLeftoversAsync()
    {
        var orphans = new FakeOrphans();
        orphans.Reports.Add(new OrphanReport(new ProcessTreeId(Guid.NewGuid()), [], OrphanDisposal.LeftRunning, [], DateTimeOffset.UnixEpoch) { Job = JobId.New() });
        using var indicator = new ResourceIndicatorViewModel(new ResourceReader(new FakeResources(), orphans, new Leftovers(), new JobBoard()), new LiveFeed(new Pulse(new JobBoard()), ui));

        indicator.Activate();

        await ui.PresentedAsync(indicator, () => indicator.HasLeftovers, () => $"{indicator.Leftovers} left over");
        Assert.Equal(("512 MB", 1), await ui.ReadAsync(() => (indicator.Memory, indicator.Leftovers)));
    }

    [Fact]
    public void WithNothingLeftOverTheIndicatorShowsNoLeftovers()
    {
        using var indicator = new ResourceIndicatorViewModel(new ResourceReader(new FakeResources(), new FakeOrphans(), new Leftovers(), new JobBoard()), new LiveFeed(new Pulse(new JobBoard()), ui));

        Assert.Equal((0, false), (indicator.Leftovers, indicator.HasLeftovers));
    }

    public void Dispose() => ui.Dispose();
}
