using Avala.Components.UI.Graphs;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class C4PlayerViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePlayerDrawsEveryElementWithItsFrameAndRelationsAndListsTheSectionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignC4PlayerViewModel(), ThemeVariant.Dark, 1240, 720);

            var graph = view.All<C4GraphPanel>().Single();
            Assert.Equal(6, view.All<TextBlock>().Count(text => text.Name == "NodeLabel"));
            Assert.Equal(["ledger"], graph.Frames.Select(frame => frame.Key));
            Assert.Equal(6, graph.Links.Count);
            Assert.Equal(["Webhooks", "Payments"], view.All<TextBlock>().Where(text => text.Name == "SectionTitle").Select(text => text.Text));
            Assert.Equal("Step 2 of 2 · Payments", view.TextOf("Position"));
        }, Cancellation);

    [Fact]
    public Task ClickingASectionMovesTheDiagramToItsStepAndBackReturnsOneStepAsync() =>
        ui.RunAsync(() =>
        {
            using var player = new C4PlayerViewModel(TimeProvider.System, new InlineUiDispatcher());
            player.Show(DesignStories.Ledger());
            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);

            view.Click(view.All<Button>().First(button => button.Name == "Section"));
            var chosen = view.TextOf("Position");
            view.Click("Back");

            Assert.Equal("Step 1 of 2 · Webhooks", chosen);
            Assert.Equal("2 steps", view.TextOf("Position"));
        }, Cancellation);
}
