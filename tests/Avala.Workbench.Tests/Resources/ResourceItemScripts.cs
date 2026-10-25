using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Resources;
using Avala.Workbench.Upkeep;
using Avala.Workspaces.Contracts;
using Avala.Sdk.Presentation;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

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
            .Then(stale => Assert.Equal(("/worktrees/old-checkout-spike", "not known to any job"), (stale.Path.ToString(), stale.Reason)));
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
    private readonly IMessenger messenger = new StrongReferenceMessenger();

    [Fact]
    public async Task TheIndicatorShowsTheMemoryInUseAndNamesWhatIsLeftOverAsync()
    {
        var orphans = new FakeOrphans();
        orphans.Reports.Add(new OrphanReport(new ProcessTreeId(Guid.NewGuid()), [], OrphanDisposal.LeftRunning, [], DateTimeOffset.UnixEpoch) { Job = JobId.New() });
        var leftovers = new Leftovers();
        await leftovers.HandleAsync(new WorktreesReconciled(new WorktreeReconciliation(["/worktrees/stray-1", "/worktrees/stray-2"], []), false), TestContext.Current.CancellationToken);
        using var indicator = Indicator(orphans, leftovers);

        indicator.Activate();

        await ui.PresentedAsync(indicator, () => indicator.HasLeftovers, () => indicator.Leftovers);
        Assert.Equal(("512 MB", "1 process left running · 2 stale worktrees"), await ui.ReadAsync(() => (indicator.Memory, indicator.Leftovers)));
    }

    [Fact]
    public async Task TheIndicatorMetersTheCpuLoadOfTheMachineAsync()
    {
        using var indicator = Indicator(new FakeOrphans());

        indicator.Activate();

        await ui.PresentedAsync(indicator, () => indicator.Cpu.Reading == "25%", () => indicator.Cpu.Reading);
        Assert.Equal(("CPU", 0.25, false), await ui.ReadAsync(() => (indicator.Cpu.Label, indicator.Cpu.Fraction, indicator.Cpu.HasThreshold)));
    }

    [Fact]
    public void OpeningTheIndicatorAsksTheShellForTheResourcesPage()
    {
        var requested = new List<IPage>();
        messenger.Register<List<IPage>, PageRequested>(requested, (list, message) => list.Add(message.Page));
        using var indicator = Indicator(new FakeOrphans());

        indicator.OpenCommand.Execute(null);

        Assert.IsType<ResourcesViewModel>(Assert.Single(requested));
    }

    [Fact]
    public void WithNothingLeftOverTheIndicatorShowsNoLeftovers()
    {
        using var indicator = Indicator(new FakeOrphans());

        Assert.Equal((string.Empty, false), (indicator.Leftovers, indicator.HasLeftovers));
    }

    public void Dispose() => ui.Dispose();

    private ResourceIndicatorViewModel Indicator(FakeOrphans orphans) => Indicator(orphans, new Leftovers());

    private ResourceIndicatorViewModel Indicator(FakeOrphans orphans, Leftovers leftovers)
    {
        var reader = new ResourceReader(new FakeResources(), orphans, leftovers, new JobBoard());
        var worktrees = new WorktreesViewModel(new WorktreeReader(new FakeResources(), new FakeHousekeeping(), new FakeWorktreeHistory(), new JobBoard()), new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance));
        var resources = new ResourcesViewModel(reader, new Housekeeping(orphans, new FakeHousekeeping()), new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance), new WorktreesEntry(new JobFocus(new TestRegions(), messenger), worktrees));

        return new ResourceIndicatorViewModel(reader, new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance), new JobFocus(new TestRegions(), messenger), resources);
    }
}
