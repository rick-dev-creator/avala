using Avala.Sdk.Telemetry;
using Avala.Testing.UI;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Telemetry;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class TelemetryViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsTheToggleTheInstallIdAndExactlyWhatIsSentAsync() =>
        ui.RunAsync(() =>
        {
            var design = new DesignTelemetryViewModel();
            var view = Screen.Show(design);

            Assert.Equal((true, design.InstallId, design.Preview), (view.Find<CheckBox>("Send").IsChecked, view.Find<SelectableTextBlock>("InstallId").Text, view.Find<SelectableTextBlock>("Preview").Text));
            Assert.Superset(new HashSet<string>(["Usage statistics", "Send usage statistics", "Install ID", "Reset ID", "Exactly what is sent, to Aptabase"]), view.VisibleTexts.ToHashSet());
        }, Cancellation);

    [Fact]
    public Task ClickingTheToggleAndResetReachTheTelemetryAsync() =>
        ui.RunAsync(async () =>
        {
            var telemetry = new FakeTelemetry();
            var model = new TelemetryViewModel(telemetry);
            await model.LoadAsync(Cancellation);
            var view = Screen.Show(model);

            view.Click("Send");
            view.Click("ResetInstallId");
            view.Settle();

            Assert.Equal((TelemetryStatus.Off, "reset-1"), (telemetry.State.Status, view.Find<SelectableTextBlock>("InstallId").Text));
            Assert.Equal((false, "Off. Nothing is sent."), (view.Find<CheckBox>("Send").IsChecked, view.TextOf("Status")));
        }, Cancellation);

    [Fact]
    public Task ABuildWithoutAKeyShowsNoInstallIdAndCannotBeTurnedOnAsync() =>
        ui.RunAsync(async () =>
        {
            var model = new TelemetryViewModel(new FakeTelemetry { State = TelemetryState.NotConfigured });
            await model.LoadAsync(Cancellation);
            var view = Screen.Show(model);

            Assert.Equal("Not configured in this build: nothing is ever sent.", view.TextOf("Status"));
            Assert.False(view.Find<CheckBox>("Send").IsEffectivelyEnabled);
            Assert.False(view.Shows("InstallId"));
            Assert.False(view.Shows("Preview"));
        }, Cancellation);
}
