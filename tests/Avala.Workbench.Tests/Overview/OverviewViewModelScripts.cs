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
using Avala.Components.Status;
using Avala.Budgets.Contracts;

namespace Avala.Workbench.Tests.Overview;

public sealed class OverviewViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();
    private readonly SessionBook sessions = new(new FakeUsage());
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
            new ConnectionsViewModel(new FleetReader(new FakeConnections("work", "personal"), usage, sessions, board), new LiveFeed(pulse, ui), Focusing.Focus(), Focusing.Spending(usage, sessions)),
            new DelegationViewModel(
                new DelegationReader(catalog, new FakeDelegations(), new JobSpending(usage, new FakeBudgets(), new FakeSupervision(), sessions), board),
                new LiveFeed(pulse, ui),
                Focusing.Focus()));
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

            public string Caption => string.Empty;

            public CommunityToolkit.Mvvm.Input.IRelayCommand<Jobs.Contracts.JobId> OpenCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand<Jobs.Contracts.JobId>(_ => { });

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

            public IOrchestratorCardViewModel? Root => null;

            public string Caption => string.Empty;

            public CommunityToolkit.Mvvm.Input.IRelayCommand<Jobs.Contracts.JobId> OpenCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand<Jobs.Contracts.JobId>(_ => { });

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
        using var page = new ConnectionsViewModel(new FleetReader(connections, new FakeUsage(), new SessionBook(new FakeUsage()), board), new LiveFeed(new Pulse(board), ui), Focusing.Focus(), Focusing.Spending(new FakeUsage(), new SessionBook(new FakeUsage())));

        page.Activate();

        await ui.PresentedAsync(page, () => page.FileNote.Length > 0, () => page.FileNote);
        Assert.Equal("connections.json is rejected: Malformed", await ui.ReadAsync(() => page.FileNote));
    }

    [Fact]
    public async Task WithoutDeclaredOrUsedConnectionsTheListIsEmptyAsync()
    {
        var board = new JobBoard();
        using var page = new ConnectionsViewModel(new FleetReader(new FakeConnections(), new FakeUsage(), new SessionBook(new FakeUsage()), board), new LiveFeed(new Pulse(board), ui), Focusing.Focus(), Focusing.Spending(new FakeUsage(), new SessionBook(new FakeUsage())));

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
                [new BoardJob(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running), Transcript.Empty)]), Option<double>.None))
            .Then(card =>
            {
                Assert.Equal(("claude-work", "Claude Code", "rick@acme.dev", true, "3.214 USD"), (card.Name, card.Provider, card.Account, card.IsDefault, card.Cost));
                Assert.Equal(("88%", "5h · 88%", false), (Assert.Single(card.Limits).UsedText, card.Use, card.IsNearLimit));
                Assert.Equal("Fix JPY rounding in invoice totals", Assert.Single(card.Agents).Title);
            });

    [Fact]
    public void AConnectionNotUsedYetSaysSo() =>
        ViewModelScript.Given(new ConnectionCardViewModel(new ConnectionState(new ConnectionName("claude-personal"), "Claude Code", false, Option<Agents.Contracts.Sessions.AgentAccount>.None, Option<UsageSummary>.None, []), Option<double>.None))
            .Then(card => Assert.Equal(("account not reported yet", "no usage yet", 0, 0), (card.Account, card.Cost, card.Limits.Count, card.Agents.Count)));
}

public sealed class ConnectionCardUpdateScripts
{
    [Fact]
    public void NewDataUpdatesTheCardAndItsAgentsInPlaceSoTheViewKeepsThem()
    {
        var fixing = new BoardJob(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running), Transcript.Empty);
        var adding = new BoardJob(Pages.Summary("Add invoice PDF endpoint", JobStatus.Running), Transcript.Empty);
        var card = new ConnectionCardViewModel(State(fixing, adding), Option<double>.None);
        var kept = card.Agents[0];

        card.Update(State(fixing with { Summary = fixing.Summary with { Status = JobStatus.Checking } }, new BoardJob(Pages.Summary("Update zod", JobStatus.Running), Transcript.Empty)), 0.9);

        Assert.Same(kept, card.Agents[0]);
        Assert.Equal((JobStatus.Checking, StatusKind.Checking, "Checking"), (kept.Status, kept.Dot.Kind, kept.State));
        Assert.Equal(["Fix JPY rounding in invoice totals", "Update zod"], card.Agents.Select(agent => agent.Title));
        Assert.Equal((true, "5h · 92%", true), (card.IsNearLimit, card.Use, card.IsProminent));
    }

    [Fact]
    public void ALimitFarFromTheHoldStaysNeutral() =>
        ViewModelScript.Given(new ConnectionCardViewModel(State(), 0.9))
            .When(card => card.Update(State(0.4), 0.9))
            .Then(card => Assert.Equal((false, 0.4, false), (card.IsNearLimit, card.Used, card.IsProminent)));

    private static ConnectionState State(params BoardJob[] agents) => State(0.92, agents);

    private static ConnectionState State(double used, params BoardJob[] agents) =>
        new(new ConnectionName("claude-work"), "Claude Code", false, Option<Agents.Contracts.Sessions.AgentAccount>.None, Pages.Used(1m, new UsageLimit("5h", used, Option<DateTimeOffset>.None)), agents);
}

public sealed class ConnectionsOpeningScripts
{
    [Fact]
    public void ChoosingAnAgentOpensItsConversationThroughTheJobFocus()
    {
        var messenger = new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger();
        var opened = new List<JobId>();
        CommunityToolkit.Mvvm.Messaging.IMessengerExtensions.Register<List<JobId>, Contracts.Presentation.JobSelected>(messenger, opened, (recipient, message) => recipient.Add(message.Job));
        var board = new JobBoard();
        using var page = new ConnectionsViewModel(new FleetReader(new FakeConnections(), new FakeUsage(), new SessionBook(new FakeUsage()), board), new LiveFeed(new Pulse(board), new TestUiDispatcher()), new Avala.Workbench.Navigation.JobFocus(new TestRegions(), messenger), Focusing.Spending(new FakeUsage(), new SessionBook(new FakeUsage())));
        var agent = new AgentViewModel(new BoardJob(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running), Transcript.Empty));

        page.OpenCommand.Execute(agent.Job);

        Assert.Equal([agent.Job], opened);
    }

    [Fact]
    public void TheCaptionCountsAgentsConnectionsAndHarnesses() =>
        Assert.Equal(
            ("10 agents · 4 connections on 3 harnesses · hover for a summary, click to open", "1 agent · 1 connection on 1 harness · hover for a summary, click to open"),
            (OverviewPhrases.Fleet(10, 4, 3), OverviewPhrases.Fleet(1, 1, 1)));
}

public sealed class OrchestratorCardViewModelScripts
{
    [Fact]
    public void TheBudgetIsCarvedBetweenWhatTheOrchestratorKeepsAndEachChild()
    {
        var root = Pages.Summary("Migrate payments to stripe-go v79", JobStatus.Running);
        var tree = new DelegationTree(root, [Child(root, 0.41m, 0.8m), Child(root, 0.22m, 0.8m), Child(root, 0.32m, 0.6m)], [])
        {
            RootSpend = new JobSpend(Option<SessionSeen>.None, Pages.Used(0.52m), new BudgetCaps([new Cost(3m, "USD")], Option<long>.None, Option<double>.None), Option<BudgetCarve>.None),
        };

        ViewModelScript.Given(new OrchestratorCardViewModel(tree))
            .Then(card => Assert.Equal(("Budget 3 USD", "1.47 USD spent across the tree", true), (card.Budget, card.Spent, card.HasBudget)))
            .Then(card => Assert.Equal([(0.8, true), (0.8, false), (0.8, false), (0.6, false)], card.Shares.Select(share => (Math.Round(share.Weight, 3), share.IsKept))))
            .Then(card => Assert.Equal("kept 0.8 USD · carved 0.8 USD · 0.8 USD · 0.6 USD", card.ShareNote))
            .Then(card => Assert.Equal("shop · waiting on 3 sub-agents", card.Detail));
    }

    [Fact]
    public void WithoutABudgetNothingIsCarved() =>
        ViewModelScript.Given(new OrchestratorCardViewModel(new DelegationTree(Pages.Summary("Ship the release", JobStatus.Running), [], [])))
            .Then(card => Assert.Equal(("No budget cap", false, 0, "0 sub-agents, no carve"), (card.Budget, card.HasBudget, card.Shares.Count, card.ShareNote)));

    internal static DelegationNode Child(JobSummary root, decimal spent, decimal carve)
    {
        var child = Pages.Summary("A child", JobStatus.Running, parent: root.Job);

        return new DelegationNode(child, 1, Option<JobFact>.None, Option<DelegationRecord>.None, new JobSpend(
            Option<SessionSeen>.None,
            Pages.Used(spent),
            Option<BudgetCaps>.None,
            new BudgetCarve(root.Job, child.Job, [new Cost(carve, "USD")], Option<long>.None, 0.25, DateTimeOffset.UnixEpoch)));
    }
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
        var sessions = new SessionBook(new FakeUsage());
        using var page = new DelegationViewModel(
            new DelegationReader(new TreeCatalog(), new FakeDelegations(), new JobSpending(usage, new FakeBudgets(), new FakeSupervision(), sessions), board),
            new LiveFeed(new Pulse(board), ui),
            Focusing.Focus());

        await page.PresentsAfterAsync(page.Activate, () => "never presented", TestContext.Current.CancellationToken);

        Assert.Equal((0, 0, true), await ui.ReadAsync(() => (page.Orchestrators.Count, page.Children.Count, page.Selected is null)));
    }

    [Fact]
    public void SelectingNothingKeepsTheSelection()
    {
        var board = new JobBoard();
        using var page = new DelegationViewModel(
            new DelegationReader(new TreeCatalog(), new FakeDelegations(), new JobSpending(new FakeUsage(), new FakeBudgets(), new FakeSupervision(), new SessionBook(new FakeUsage())), board),
            new LiveFeed(new Pulse(board), ui),
            Focusing.Focus());

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
            .Then(node => Assert.Equal(("not started", "Preparing", "no cost", "no carve", string.Empty, 2, 0d), (node.Connection, node.Activity, node.Spent, node.Carve, node.Harness, node.Depth, node.SpentShare)));

    [Theory]
    [InlineData(0.4, 0.8, 0.5)]
    [InlineData(1.2, 0.8, 1)]
    public void AChildsSpendIsMeasuredAgainstItsCarve(double spent, double carve, double share) =>
        ViewModelScript.Given(new DelegationNodeViewModel(OrchestratorCardViewModelScripts.Child(Pages.Summary("Root", JobStatus.Running), (decimal)spent, (decimal)carve)))
            .Then(node => Assert.Equal(share, node.SpentShare, 3))
            .Then(node => Assert.Equal((StatusKind.Working, "Running"), (node.Dot.Kind, node.State)));
}

internal static class Focusing
{
    public static Avala.Workbench.Navigation.JobFocus Focus() => new(new TestRegions(), new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger());

    public static JobSpending Spending(FakeUsage usage, SessionBook sessions) => new(usage, new FakeBudgets(), new FakeSupervision(), sessions);
}
