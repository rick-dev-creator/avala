using Avala.Testing.UI;
using Avala.Workbench.Inspector;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class ContestSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheContestSectionShowsTheCandidatesPlaceTheContestsStatusAndEveryStandingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new DesignContestSectionViewModel()));

            Assert.Equal(((object?)"Contest", (string?)"candidate 3 of 3 · the winner", true), Sections.Row(view));
            Assert.Equal(("Decided: the winner waits for your review", 3), (view.TextOf("Status"), view.Find<ItemsControl>("Ranking").ItemCount));
        }, TestContext.Current.CancellationToken);
}
