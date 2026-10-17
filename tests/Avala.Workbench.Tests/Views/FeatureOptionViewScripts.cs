using Avala.Testing.UI;
using Avala.Workbench.NewJob;
using Shape = Avalonia.Controls.Shapes.Path;

namespace Avala.Workbench.Tests.Views;

public sealed class FeatureOptionViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnOptionShowsItsTitleAndFactAndChecksOnlyTheChosenOneAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = Screen.Show(new DesignFeatureOptionViewModel("Invoices", "3 versions", IsNew: false, IsNone: false, IsChosen: true));
            var other = Screen.Show(new DesignFeatureOptionViewModel(FeaturePhrases.DontDocument, string.Empty, IsNew: false, IsNone: true, IsChosen: false));

            Assert.Equal(("Invoices", "3 versions", true), (chosen.TextOf("OptionTitle"), chosen.TextOf("OptionFact"), chosen.Find<Shape>("OptionCheck").IsVisible));
            Assert.Equal((FeaturePhrases.DontDocument, false, false), (other.TextOf("OptionTitle"), other.Shows("OptionFact"), other.Find<Shape>("OptionCheck").IsVisible));
        }, Cancellation);
}
