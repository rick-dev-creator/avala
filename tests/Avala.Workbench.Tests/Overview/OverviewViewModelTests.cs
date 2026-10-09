using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Tests.Overview;

public sealed class OverviewViewModelTests : IDisposable
{
    private readonly TestUiDispatcher ui = new();
    private readonly SessionBook sessions = new();
    private readonly TreeCatalog catalog = new();
    private readonly JobBoard board = new();

    [Fact]
    public async Task WhileActiveTheConnectionsShowTheirAgentsAndTheLatestOrchestratorsTreeUntilAnotherIsSelectedAsync()
    {
        var first = Pages.Summary("Ship the release", JobStatus.Running);
        var second = Pages.Summary("Refactor the module", JobStatus.Running);
        catalog.Jobs.AddRange([first, Pages.Summary("Write the notes", JobStatus.Running, parent: first.Job), second, Pages.Summary("Rename the types", JobStatus.Running, parent: second.Job)]);
        _ = await sessions.OpenAsync("work", first.Job);
        board.Publish(Pages.Board(first).Jobs);
        var overview = Overview();

        overview.Activate();
        await ui.UntilAsync(() => overview.Connections.Connections.Count == 2 && overview.Delegation.Children.Count == 1);
        var latest = await ui.ReadAsync(() => (overview.Delegation.Selected?.Title, overview.Delegation.Children[0].Title));
        await ui.InvokeAsync(() => overview.Delegation.SelectCommand.Execute(overview.Delegation.Orchestrators[0]), TestContext.Current.CancellationToken);
        await ui.UntilAsync(() => overview.Delegation.Children.Count == 1 && overview.Delegation.Children[0].Title == "Write the notes");

        Assert.Equal(("Refactor the module", "Rename the types"), latest);
        Assert.Equal(
            ["Ship the release"],
            await ui.ReadAsync(() => overview.Connections.Connections[0].Agents.Select(agent => agent.Title).ToList()));
        overview.Deactivate();
    }

    [Fact]
    public void TheOverviewOpensOnTheConnectionsAndSwitchesToTheDelegationTree()
    {
        var overview = Overview();
        var opened = overview.ShowsDelegation;

        overview.ShowDelegationCommand.Execute(null);

        Assert.Equal((false, true), (opened, overview.ShowsDelegation));
    }

    public void Dispose() => ui.Dispose();

    private OverviewViewModel Overview()
    {
        var usage = new FakeUsage();
        var pulse = new Pulse(board);

        return new OverviewViewModel(
            new ConnectionsViewModel(new FleetReader(new FakeConnections("work", "personal"), usage, sessions, board), new LiveFeed(pulse, ui)),
            new DelegationViewModel(
                new DelegationReader(catalog, new FakeDelegations(), new JobSpending(usage, new FakeBudgets(), new FakeSupervision(), sessions), board),
                new LiveFeed(pulse, ui)));
    }
}
