using Avala.Testing.UI;
using Avala.Workbench.ModelChoices;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class ModelPickerViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnOfferShowsTheModelAndEffortPickersWithTheirChoicesAndNoteAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignModelPickerViewModel());

            Assert.Equal((true, true, true), (view.Shows("Model"), view.Shows("Effort"), view.Shows("ModelNote")));
            Assert.Equal(("opus", "high"), (view.Find<ComboBox>("Model").SelectedItem, view.Find<ComboBox>("Effort").SelectedItem));
            Assert.Equal((4, 6), (view.Find<ComboBox>("Model").ItemCount, view.Find<ComboBox>("Effort").ItemCount));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AHarnessWithoutEffortsShowsOnlyTheModelPickerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignModelPickerViewModel { IsEffortShown = false });

            Assert.Equal((true, false), (view.Shows("Model"), view.Shows("Effort")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task FollowingAutoShowsTheDefaultDisabledAndHidingShowsOnlyTheNoteAsync() =>
        ui.RunAsync(() =>
        {
            var following = Screen.Show(new DesignModelPickerViewModel { CanChoose = false, Models = ["Default (sonnet)"], Model = "Default (sonnet)" });
            var hidden = Screen.Show(new DesignModelPickerViewModel { IsShown = false, Note = "work offers no choice of model: its harness runs its own." });

            Assert.Equal((true, false), (following.Shows("Model"), following.Find<ComboBox>("Model").IsEnabled));
            Assert.Equal((false, "work offers no choice of model: its harness runs its own."), (hidden.Shows("Model"), hidden.TextOf("ModelNote")));
        }, TestContext.Current.CancellationToken);
}
