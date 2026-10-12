using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Welcome;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class HarnessChoiceViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheChipShowsTheMonogramAndWhatRunsAndExplainsWhyOnHoverAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignHarnessChoiceViewModel());

            Assert.Equal(("CW", "Auto · claude-work", false), (view.TextOf("HarnessMonogram"), view.TextOf("HarnessLabel"), view.Find<Popup>("HarnessMenu").IsOpen));
            Assert.Equal("Most capacity left · 12% of the 5-hour window used", ToolTip.GetTip(view.Find("HarnessButton")));
        }, Cancellation);

    [Fact]
    public Task ClickingTheChipOpensTheMenuWithAutoAndTheConnectionsAndNoModelToPickUnderAutoAsync() =>
        ui.RunAsync(() =>
        {
            var harness = new DesignHarnessChoiceViewModel();
            var view = Screen.Show(harness, ThemeVariant.Dark, 900, 700);

            view.Click("HarnessButton");

            Assert.True(harness.IsOpen);
            Assert.Equal(["Auto", "claude-work", "claude-personal"], view.All<TextBlock>().Where(text => text.Name == "OptionName").Select(text => text.Text));
            Assert.False(view.Shows("HarnessModels"));
            Assert.Single(view.All<Avalonia.Controls.Shapes.Path>(), path => path.Name == "OptionCheck");
        }, Cancellation);

    [Fact]
    public Task ClickingAConnectionInTheMenuChoosesItAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = new List<Option<ConnectionName>>();
            var view = Screen.Show(new HarnessOptionViewModel("claude-work", new ConnectionName("claude-work"), chosen.Add));

            view.Click("Option");

            Assert.Equal([new ConnectionName("claude-work")], chosen.Select(name => name.Match(found => found, () => new ConnectionName("none"))));
            Assert.Equal("CW", view.TextOf("OptionMonogram"));
        }, Cancellation);

    [Fact]
    public Task AChosenConnectionOffersItsModelsInTheMenuAndPickingOneKeepsTheMenuOpenAsync() =>
        ui.RunAsync(() =>
        {
            var models = new DesignModelPickerViewModel { Model = "Default (sonnet)" };
            var harness = new DesignHarnessChoiceViewModel { Models = models };
            var view = Screen.Show(harness, ThemeVariant.Dark, 900, 700);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            view.Click("HarnessButton");

            var offered = view.Shows("HarnessModels");
            view.Click("Model");
            var opus = view.All<ComboBoxItem>().Single(item => Equals(item.Content, "opus"));
            view.Click(opus);

            Assert.Equal((true, "opus", true), (offered, models.Model, harness.IsOpen));
        }, Cancellation);
}
