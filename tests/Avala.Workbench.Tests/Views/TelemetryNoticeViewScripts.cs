using Avala.Sdk.Telemetry;
using Avala.Testing.UI;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Onboarding;
using Avala.Workbench.Tests.Telemetry;

namespace Avala.Workbench.Tests.Views;

public sealed class TelemetryNoticeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheNoticeSaysWhatIsSentAndTurnOffTurnsItOffAsync() =>
        ui.RunAsync(async () =>
        {
            var telemetry = new FakeTelemetry();
            var notice = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding { IsDone = true });
            notice.Activate();
            await notice.Claiming;
            var view = Screen.Show(notice);
            var said = view.TextOf("Text");

            view.Click("TurnOff");
            view.Settle();

            Assert.Equal(TelemetryPhrases.Notice, said);
            Assert.Equal(TelemetryStatus.Off, telemetry.State.Status);
            Assert.False(view.Shows("Notice"));
        }, Cancellation);

    [Fact]
    public Task OkHidesTheNoticeAsync() =>
        ui.RunAsync(async () =>
        {
            var notice = new TelemetryNoticeViewModel(new FakeTelemetry(), new FakeOnboarding { IsDone = true });
            notice.Activate();
            await notice.Claiming;
            var view = Screen.Show(notice);
            var before = view.Shows("Notice");

            view.Click("Dismiss");
            view.Settle();

            Assert.True(before);
            Assert.False(view.Shows("Notice"));
        }, Cancellation);
}
