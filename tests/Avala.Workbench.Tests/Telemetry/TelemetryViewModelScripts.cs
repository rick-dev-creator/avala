using Avala.Sdk;
using Avala.Sdk.Telemetry;
using Avala.Workbench.Telemetry;

namespace Avala.Workbench.Tests.Telemetry;

public sealed class TelemetryViewModelScripts
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(nameof(TelemetryStatus.On), "", "On. Exactly the event below is sent, and nothing else.", true, true)]
    [InlineData(nameof(TelemetryStatus.Off), "", "Off. Nothing is sent.", false, true)]
    [InlineData(nameof(TelemetryStatus.OffByEnvironment), "DO_NOT_TRACK=1", "Off because DO_NOT_TRACK=1 is set in Avala's environment. Nothing is sent.", false, false)]
    [InlineData(nameof(TelemetryStatus.OffByEnvironment), "AVALA_TELEMETRY=0", "Off because AVALA_TELEMETRY=0 is set in Avala's environment. Nothing is sent.", false, false)]
    [InlineData(nameof(TelemetryStatus.NotConfigured), "", "Not configured in this build: nothing is ever sent.", false, false)]
    public async Task ItSaysWhetherUsageStatisticsAreSentAndWhyAsync(string status, string variable, string said, bool on, bool canTurn)
    {
        var telemetry = new FakeTelemetry { State = new TelemetryState(Enum.Parse<TelemetryStatus>(status), "id", variable.Length > 0 ? variable : Option<string>.None) };
        var view = new TelemetryViewModel(telemetry);

        await view.LoadAsync(Cancellation);

        Assert.Equal((said, on, canTurn), (view.Status, view.IsOn, view.ToggleCommand.CanExecute(null)));
    }

    [Fact]
    public async Task LoadingShowsTheInstallIdAndExactlyWhatIsSentAsync()
    {
        var telemetry = new FakeTelemetry();
        var view = new TelemetryViewModel(telemetry);

        await view.LoadAsync(Cancellation);

        Assert.Equal("3f2b8c1e-7d4a-4e09-9b6f-52c1a0d8e7f4", view.InstallId);
        Assert.Equal(await telemetry.PreviewAsync(Cancellation), view.Preview);
    }

    [Fact]
    public async Task TogglingTurnsSendingOffAndOnAgainAsync()
    {
        var telemetry = new FakeTelemetry();
        var view = new TelemetryViewModel(telemetry);
        await view.LoadAsync(Cancellation);

        await view.ToggleCommand.ExecuteAsync(null);
        var off = (view.IsOn, telemetry.State.Status);
        await view.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((false, TelemetryStatus.Off), off);
        Assert.Equal((true, TelemetryStatus.On), (view.IsOn, telemetry.State.Status));
    }

    [Fact]
    public async Task ResettingTheInstallIdShowsTheNewIdInThePreviewAsync()
    {
        var view = new TelemetryViewModel(new FakeTelemetry());
        await view.LoadAsync(Cancellation);

        await view.ResetInstallIdCommand.ExecuteAsync(null);

        Assert.Equal(("reset-1", FakeTelemetry.Payload("reset-1")), (view.InstallId, view.Preview));
    }
}
