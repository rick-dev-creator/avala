using Avala.Testing.UI;
using Avala.Workbench.NewJob;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class FeatureChipViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheChipShowsTheFeatureAndWhetherItIsNewWithItsMenuClosedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFeatureChipViewModel());

            Assert.Equal(("Invoice PDF endpoint", FeaturePhrases.New, false), (view.TextOf("FeatureName"), view.TextOf("FeatureFact"), view.Find<Popup>("FeatureMenu").IsOpen));
        }, Cancellation);

    [Fact]
    public Task ClickingTheChipOpensTheMenuWithTheSuggestionTheFeaturesAndNotDocumentingTheChosenOneCheckedAsync() =>
        ui.RunAsync(() =>
        {
            var chip = new DesignFeatureChipViewModel();
            var view = Screen.Show(chip, ThemeVariant.Dark, 700, 500);

            view.Click("FeatureButton");

            Assert.True(chip.IsOpen);
            Assert.Equal(["Invoice PDF endpoint", "Invoices", FeaturePhrases.DontDocument], view.All<TextBlock>().Where(text => text.Name == "OptionTitle").Select(text => text.Text));
            Assert.Single(view.All<Avalonia.Controls.Shapes.Path>(), path => path.Name == "OptionCheck" && path.IsEffectivelyVisible);
        }, Cancellation);

    [Fact]
    public Task NewJobShowsTheChipUnderTheInstructionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNewJobViewModel(), ThemeVariant.Dark, 900, 900);

            Assert.Equal("Invoice PDF endpoint", view.TextOf("FeatureName"));
            Assert.True(view.Find("FeatureButton").TranslatePoint(default, view.Window)?.Y > view.Find("Instruction").TranslatePoint(default, view.Window)?.Y);
        }, Cancellation);
}
