using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class UsageLimitsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsThePreparePointTheCapAndTheConnectionsInTheRelayAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLimitsViewModel(), ThemeVariant.Dark, 720, 900);

            Assert.Equal(("90%", "97%", "limits.json: applied"), (view.TextOf("ThresholdLabel"), view.TextOf("HardCapLabel"), view.TextOf("File")));
            Assert.True(view.Find<StackPanel>("Relaying").IsVisible);
            Assert.Equal(4, view.All<CheckBox>().Count(row => row.Name == "Included"));
            Assert.False(view.Find<Button>("Save").IsVisible);
        }, Cancellation);

    [Fact]
    public Task HoldingHidesTheRelayAndAChangedDraftOffersSaveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLimitsViewModel { HandsOff = false, IsChanged = true }, ThemeVariant.Dark, 720, 900);

            Assert.False(view.Find<StackPanel>("Relaying").IsVisible);
            Assert.True(view.Find<Button>("Save").IsVisible);
        }, Cancellation);
}
