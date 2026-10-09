using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Overview;

public sealed class OverviewViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();
    private readonly SessionBook sessions = new();
    private readonly TreeCatalog catalog = new();
    private readonly JobBoard board = new();

    [Fact]
    public async Task WhileActiveTheConnectionsShowTheirAgentsAndTheLatestOrchestratorsTreeUntilAnotherIsSelectedAsync()
    {
        var first = Pages.Summary("Ship the release", JobStatus.Running);
        var second = Pages.Summary("Split CheckoutPage into steps", JobStatus.Running);
        catalog.Jobs.AddRange([first, Pages.Summary("Write the notes", JobStatus.Running, parent: first.Job), second, Pages.Summary("Extract the address step", JobStatus.Running, parent: second.Job)]);
        _ = await sessions.OpenAsync("work", first.Job);
        board.Publish(Pages.Board(first).Jobs);
        var overview = Overview();

        overview.Activate();
        await ui.PresentedAsync(overview.Connections, () => overview.Connections.Connections.Count == 2, () => $"{overview.Connections.Connections.Count} connections");
        await ui.PresentedAsync(overview.Delegation, () => overview.Delegation.Children.Count == 1, () => $"{overview.Delegation.Children.Count} children");
        var latest = await ui.ReadAsync(() => (overview.Delegation.Selected?.Title, overview.Delegation.Children[0].Title));
        await ui.InvokeAsync(() => overview.Delegation.SelectCommand.Execute(overview.Delegation.Orchestrators[0]), TestContext.Current.CancellationToken);
        await ui.PresentedAsync(overview.Delegation, () => overview.Delegation.Children[0].Title == "Write the notes", () => overview.Delegation.Children[0].Title);

        Assert.Equal(("Split CheckoutPage into steps", "Extract the address step"), latest);
        Assert.Equal(["Ship the release"], await ui.ReadAsync(() => overview.Connections.Connections[0].Agents.Select(agent => agent.Title).ToList()));
        overview.Deactivate();
    }

    [Fact]
    public void TheOverviewOpensOnTheConnectionsAndSwitchesBetweenItsTwoViews() =>
        ViewModelScript.Given(Overview())
            .Then(overview => Assert.Equal(("Overview", false), (overview.Title, overview.ShowsDelegation)))
            .Invoke(nameof(OverviewViewModel.ShowDelegationCommand))
            .ThenNotified(nameof(OverviewViewModel.ShowsDelegation))
            .Then(overview => Assert.True(overview.ShowsDelegation))
            .Invoke(nameof(OverviewViewModel.ShowConnectionsCommand))
            .Then(overview => Assert.False(overview.ShowsDelegation));

    [Fact]
    public void ActivatingThePageActivatesBothViewsAndDeactivatingStopsThem()
    {
        var connections = new Activations();
        var delegation = new Activations();
        var overview = new OverviewViewModel(connections.Connections, delegation.Delegation);

        overview.Activate();
        overview.Deactivate();

        Assert.Equal(["activated", "deactivated"], connections.Calls);
        Assert.Equal(["activated", "deactivated"], delegation.Calls);
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

    private sealed class Activations
    {
        public List<string> Calls { get; } = [];

        public IConnectionsViewModel Connections => new ActivatedConnections(Calls);

        public IDelegationViewModel Delegation => new ActivatedDelegation(Calls);

        private sealed class ActivatedConnections(List<string> calls) : IConnectionsViewModel
        {
            public IReadOnlyList<IConnectionCardViewModel> Connections => [];

            public string FileNote => string.Empty;

            public long Revision => 0;

            public event EventHandler<Sdk.Presentation.Presented>? Presented
            {
                add { }
                remove { }
            }

            public void Activate() => calls.Add("activated");

            public void Deactivate() => calls.Add("deactivated");
        }

        private sealed class ActivatedDelegation(List<string> calls) : IDelegationViewModel
        {
            public IReadOnlyList<IOrchestratorViewModel> Orchestrators => [];

            public IReadOnlyList<IDelegationNodeViewModel> Children => [];

            public IReadOnlyList<IDelegationRefusalViewModel> Refused => [];

            public IOrchestratorViewModel? Selected => null;

            public CommunityToolkit.Mvvm.Input.IRelayCommand<IOrchestratorViewModel> SelectCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand<IOrchestratorViewModel>(_ => { });

            public long Revision => 0;

            public event EventHandler<Sdk.Presentation.Presented>? Presented
            {
                add { }
                remove { }
            }

            public void Activate() => calls.Add("activated");

            public void Deactivate() => calls.Add("deactivated");
        }
    }
}

public sealed class ConnectionsViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task ARejectedConnectionsFileIsNotedAboveTheConnectionsAsync()
    {
        var board = new JobBoard();
        var connections = new FakeConnections("work") { Catalog = new FakeConnections("work").Catalog with { File = ConnectionFileStatus.Rejected, Error = ConnectionError.Malformed } };
        using var page = new ConnectionsViewModel(new FleetReader(connections, new FakeUsage(), new SessionBook(), board), new LiveFeed(new Pulse(board), ui));

        page.Activate();

        await ui.PresentedAsync(page, () => page.FileNote.Length > 0, () => page.FileNote);
        Assert.Equal("connections.json is rejected: Malformed", await ui.ReadAsync(() => page.FileNote));
    }

    [Fact]
    public async Task WithoutDeclaredOrUsedConnectionsTheListIsEmptyAsync()
    {
        var board = new JobBoard();
        using var page = new ConnectionsViewModel(new FleetReader(new FakeConnections(), new FakeUsage(), new SessionBook(), board), new LiveFeed(new Pulse(board), ui));

        await page.PresentsAfterAsync(page.Activate, () => "never presented", TestContext.Current.CancellationToken);

        Assert.Empty(await ui.ReadAsync(() => page.Connections.ToList()));
    }

    public void Dispose() => ui.Dispose();
}

public sealed class ConnectionCardViewModelScripts
{
    [Fact]
    public void ACardShowsItsConnectionAccountCostLimitsAndAgents() =>
        ViewModelScript.Given(new ConnectionCardViewModel(new ConnectionState(
                new ConnectionName("claude-work"),
                "Claude Code",
                true,
                new Agents.Contracts.Sessions.AgentAccount("rick@acme.dev", "rick@acme.dev"),
                Pages.Used(3.214m, new UsageLimit("5h", 0.88, Option<DateTimeOffset>.None)),
                [new BoardJob(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running), Transcript.Empty)])))
            .Then(card =>
            {
                Assert.Equal(("claude-work", "Claude Code", "rick@acme.dev", true, "3.214 USD"), (card.Name, card.Provider, card.Account, card.IsDefault, card.Cost));
                Assert.Equal("88% used", Assert.Single(card.Limits).UsedText);
                Assert.Equal("Fix JPY rounding in invoice totals", Assert.Single(card.Agents).Title);
            });

    [Fact]
    public void AConnectionNotUsedYetSaysSo() =>
        ViewModelScript.Given(new ConnectionCardViewModel(new ConnectionState(new ConnectionName("claude-personal"), "Claude Code", false, Option<Agents.Contracts.Sessions.AgentAccount>.None, Option<UsageSummary>.None, [])))
            .Then(card => Assert.Equal(("account not reported yet", "no usage yet", 0, 0), (card.Account, card.Cost, card.Limits.Count, card.Agents.Count)));
}

public sealed class AgentViewModelScripts
{
    [Fact]
    public void AnAgentShowsItsJobAndWhatItIsDoing() =>
        ViewModelScript.Given(new AgentViewModel(new BoardJob(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running), Transcript.Empty)))
            .Then(agent => Assert.Equal(("Fix JPY rounding in invoice totals", JobStatus.Running, "working"), (agent.Title, agent.Status, agent.Fact)));
}

public sealed class DelegationViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task WithoutOrchestratorsNothingIsSelectedAsync()
    {
        var board = new JobBoard();
        var usage = new FakeUsage();
        var sessions = new SessionBook();
        using var page = new DelegationViewModel(
            new DelegationReader(new TreeCatalog(), new FakeDelegations(), new JobSpending(usage, new FakeBudgets(), new FakeSupervision(), sessions), board),
            new LiveFeed(new Pulse(board), ui));

        await page.PresentsAfterAsync(page.Activate, () => "never presented", TestContext.Current.CancellationToken);

        Assert.Equal((0, 0, true), await ui.ReadAsync(() => (page.Orchestrators.Count, page.Children.Count, page.Selected is null)));
    }

    [Fact]
    public void SelectingNothingKeepsTheSelection()
    {
        var board = new JobBoard();
        using var page = new DelegationViewModel(
            new DelegationReader(new TreeCatalog(), new FakeDelegations(), new JobSpending(new FakeUsage(), new FakeBudgets(), new FakeSupervision(), new SessionBook()), board),
            new LiveFeed(new Pulse(board), ui));

        page.SelectCommand.Execute(null);

        Assert.Null(page.Selected);
    }

    public void Dispose() => ui.Dispose();
}

public sealed class OrchestratorViewModelScripts
{
    [Fact]
    public void AnOrchestratorShowsItsTitleAndStatus() =>
        ViewModelScript.Given(new OrchestratorViewModel(Pages.Summary("Split CheckoutPage into steps\nKeep each step testable", JobStatus.Running)))
            .Then(orchestrator => Assert.Equal(("Split CheckoutPage into steps", JobStatus.Running), (orchestrator.Title, orchestrator.Status)));
}

public sealed class DelegationRefusalViewModelScripts
{
    [Fact]
    public void ARefusalSaysWhatWasAskedAndWhyItWasRefused() =>
        ViewModelScript.Given(new DelegationRefusalViewModel(new DelegationRecord(Agents.Contracts.Sessions.SessionId.New(), new Agents.Contracts.Sessions.ItemId("delegate"), "Rewrite the payment step in Svelte", DateTimeOffset.UnixEpoch) { Refusal = DelegationError.DepthExceeded }))
            .Then(refusal => Assert.Equal(("Rewrite the payment step in Svelte", "too deep"), (refusal.Instruction, refusal.Reason)));
}

public sealed class DelegationNodeViewModelScripts
{
    [Fact]
    public void AChildNotStartedYetShowsItsStatusAndNoCarve() =>
        ViewModelScript.Given(new DelegationNodeViewModel(new DelegationNode(
                Pages.Summary("Write the payment step's tests", JobStatus.Preparing),
                2,
                Option<JobFact>.None,
                Option<DelegationRecord>.None,
                new JobSpend(Option<SessionSeen>.None, Option<UsageSummary>.None, Option<Budgets.Contracts.BudgetCaps>.None, Option<Budgets.Contracts.BudgetCarve>.None))))
            .Then(node => Assert.Equal(("not started", "Preparing", "no cost", "no carve", string.Empty, 2), (node.Connection, node.Activity, node.Spent, node.Carve, node.Harness, node.Depth)));
}
