using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Fleet;
using Avala.Workbench.Overview;
using Avala.Workbench.Tests.Overview;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Avala.Components.UI.Graphs;
using Avala.Components.UI.Theme;

namespace Avala.Workbench.Tests.Views;

public sealed class AgentTreeMotionViewScripts(HeadlessUi ui) : IDisposable
{
    private static readonly TimeSpan Arrival = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan Settling = TimeSpan.FromMilliseconds(320);

    private readonly TreeBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASubAgentThatStartsInALiveTreeFadesInUnderItsParentAsync()
    {
        var (before, after) = await StartingAsync();

        await ui.RunAsync(() =>
        {
            var (view, panel) = Live(before, after, reduced: false);
            var arriving = Card(panel, "Check the refund rounding").Opacity;
            view.Elapse(Arrival);

            Assert.Equal((0d, 1d), (arriving, Card(panel, "Check the refund rounding").Opacity));
        }, Cancellation);
    }

    [Fact]
    public async Task WithReducedMotionASubAgentThatStartsInALiveTreeIsWhollyShownAtOnceAsync()
    {
        var (before, after) = await StartingAsync();

        await ui.RunAsync(() =>
        {
            var (_, panel) = Live(before, after, reduced: true);

            Assert.Equal(1d, Card(panel, "Check the refund rounding").Opacity);
            Assert.Contains(panel.Drawn.Edges, edge => edge.Node == panel.Children.IndexOf(Card(panel, "Check the refund rounding")));
        }, Cancellation);
    }

    [Fact]
    public Task ASubAgentThatSettlesEasesItsCardToTheDimmedOpacityAsync() =>
        ui.RunAsync(() =>
        {
            var (view, place) = Agent(reduced: false);
            var card = view.Find<Border>("Card");

            place.Show(1, BranchKind.InProcess, SubagentState.Completed, 0, false);
            view.Settle();
            var easing = card.Opacity;
            view.Elapse(Settling);

            Assert.Equal((1d, 0.72), (easing, card.Opacity));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionASubAgentThatSettlesDimsAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var (view, place) = Agent(reduced: true);

            place.Show(1, BranchKind.InProcess, SubagentState.Completed, 0, false);
            view.Settle();

            Assert.Equal(0.72, view.Find<Border>("Card").Opacity);
        }, Cancellation);

    [Fact]
    public Task TheStateChipFadesItsNewMarkInWhenTheStateChangesAsync() =>
        ui.RunAsync(() =>
        {
            var (view, place) = Agent(reduced: false);

            place.Show(1, BranchKind.InProcess, SubagentState.Failed, 0, false);
            view.Settle();
            var swapping = view.Find("Mark").Opacity;
            view.Elapse(TimeSpan.FromMilliseconds(180));

            Assert.Equal((0d, 1d, "failed"), (swapping, view.Find("Mark").Opacity, view.TextOf("StateText")));
        }, Cancellation);

    public void Dispose() => bench.Dispose();

    private async Task<(DelegationTree Before, DelegationTree After)> StartingAsync()
    {
        var before = await bench.ReadAsync();
        bench.Starts(bench.Endpoint, "rounding", "Check the refund rounding");

        return (before, await bench.ReadAsync());
    }

    private (ViewScript View, AgentTreePanel Panel) Live(DelegationTree before, DelegationTree after, bool reduced)
    {
        var tree = bench.Tree();
        tree.Show(before, Option<JobId>.None);
        var view = Screen.Show(new AgentTreeSamples.Pane(tree), ThemeVariant.Dark, 1180, 900);
        Motion.SetIsReduced(view.Window, reduced);
        tree.Show(after, Option<JobId>.None);
        view.Settle();

        return (view, view.All<AgentTreePanel>().Single());
    }

    private static Control Card(AgentTreePanel panel, string name) =>
        panel.Children.Single(child => child is ContentPresenter { Content: ISubagentViewModel agent } && agent.Name == name);

    private static (ViewScript View, BranchPlaceViewModel Place) Agent(bool reduced)
    {
        var place = new BranchPlaceViewModel("agent", _ => { });
        place.Show(1, BranchKind.InProcess, SubagentState.Working, 0, false);
        var view = Screen.Show(new SubagentViewModel(place, JobId.New()));
        Motion.SetIsReduced(view.Window, reduced);

        return (view.Settle(), place);
    }
}
