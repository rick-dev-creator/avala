using Avala.Components.UI.Graphs;
using Avala.Components.Status;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class AgentTreeAdversaryViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task EveryCardOfADeepTreeFitsTheNarrowPaneWithoutBeingClippedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new AgentTreeSamples.Pane(new DesignSubagentTreeViewModel { Branches = AgentTreeSamples.Deep() }), ThemeVariant.Dark, 380, 1400);
            var viewport = view.All<ScrollViewer>()[0];
            var right = viewport.TranslatePoint(new Point(viewport.Bounds.Width, 0), view.Window)?.X ?? 0;
            var panel = view.All<AgentTreePanel>().Single();
            var panelLeft = panel.TranslatePoint(default, view.Window)?.X ?? 0;

            var clipped = panel.Children
                .Select(slot => panelLeft + slot.Bounds.Right)
                .Where(edge => edge > right)
                .ToList();

            Assert.Empty(clipped);
        }, Cancellation);

    [Fact]
    public Task TheOrchestratorCardKeepsItsTreeTokensInsideTheCardOnceItsContextCompactedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorCardViewModel(), ThemeVariant.Dark, 1440, 600);
            var card = view.Find<Border>("Card");
            var tokens = view.Find<TextBlock>("Tokens");
            var cardRight = card.TranslatePoint(new Point(card.Bounds.Width - card.Padding.Right, 0), view.Window)?.X ?? 0;
            var tokensRight = tokens.TranslatePoint(new Point(tokens.Bounds.Width, 0), view.Window)?.X ?? 0;

            Assert.True(tokensRight <= cardRight, $"tokens end at {tokensRight}, the card's content at {cardRight}");
        }, Cancellation);

    [Fact]
    public Task TheOrchestratorCardOffersToFoldTheBranchesItHoldsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorCardViewModel(), ThemeVariant.Dark, 1440, 600);

            Assert.Contains(view.All<Button>(), button => button.Name == "Fold");
        }, Cancellation);
}

internal static class AgentTreeSamples
{
    public static IReadOnlyList<IAgentBranch> Deep() =>
    [
        new DesignOrchestratorCardViewModel(),
        Node("Rewrite the whole webhook signature verification suite against the new provider contract and its fixtures", 1, BranchKind.CrossHarness, SubagentState.Waiting, "Codex", "a-very-long-connection-name-for-codex-pro"),
        Node("Grandchild job", 2, BranchKind.Child, SubagentState.Working, "Claude Code", "claude-work"),
        Node("Great-grandchild job", 3, BranchKind.Child, SubagentState.Failed, "Claude Code", "claude-work"),
        new DesignSubagentViewModel("Its in-process sub-agent four levels down", "Grep RefundParams in internal/", SubagentState.Waiting)
        {
            Place = new DesignBranchPlaceViewModel("deep", 4, BranchKind.InProcess, SubagentState.Waiting),
            Note = "asks: go generate ./internal/webhooks/... && go test ./internal/webhooks/... -run TestSignatureVerification",
        },
        Node("Second first-level job", 1, BranchKind.Child, SubagentState.Working, "Claude Code", "claude-work"),
        new DesignSubagentViewModel("Sub-agent of the second", "Read internal/idempotency/keys.go", SubagentState.Lost)
        {
            Place = new DesignBranchPlaceViewModel("lost", 2, BranchKind.InProcess, SubagentState.Lost),
            Note = "its work was lost when its turn or session ended",
        },
        Node("Third first-level job", 1, BranchKind.Child, SubagentState.Completed, "Claude Code", "claude-work"),
        Node("Fourth first-level job", 1, BranchKind.Child, SubagentState.Stopped, "Claude Code", "claude-work"),
    ];

    private static DesignDelegationNodeViewModel Node(string title, int depth, BranchKind kind, SubagentState state, string harness, string connection) =>
        new(JobId.New(), title, depth, StatusKind.Working, harness, connection, "Doing things", "0.10 USD", false, 0)
        {
            Place = new DesignBranchPlaceViewModel(title, depth, kind, state) { Branches = 1 },
        };

    internal sealed class Pane(ISubagentTreeViewModel tree) : IDelegationPaneViewModel
    {
        public ISubagentTreeViewModel Tree => tree;

        public bool HasTree => true;

        public string Caption => "5 jobs · 2 agents";

        public string Footer => "claude-work · 4 jobs";

        public bool IsOpen => true;

        public CommunityToolkit.Mvvm.Input.IRelayCommand CloseCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
    }
}
