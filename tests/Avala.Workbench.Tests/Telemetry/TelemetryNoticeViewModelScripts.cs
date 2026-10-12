using Avala.Sdk.Telemetry;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Onboarding;

namespace Avala.Workbench.Tests.Telemetry;

public sealed class TelemetryNoticeViewModelScripts
{
    [Fact]
    public async Task TheNoticeIsShownOnlyTheFirstTimeAsync()
    {
        var telemetry = new FakeTelemetry();
        var first = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding { IsDone = true });
        var later = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding { IsDone = true });

        first.Activate();
        await first.Claiming;
        later.Activate();
        await later.Claiming;

        Assert.Equal((true, false), (first.IsShown, later.IsShown));
        Assert.Equal("Avala sends its version, your OS and a random install ID to Aptabase, at most once a day.", first.Text);
    }

    [Fact]
    public async Task TurnOffTurnsUsageStatisticsOffAndHidesTheNoticeAsync()
    {
        var telemetry = new FakeTelemetry();
        var notice = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding { IsDone = true });
        notice.Activate();
        await notice.Claiming;

        await notice.TurnOffCommand.ExecuteAsync(null);

        Assert.Equal((false, TelemetryStatus.Off), (notice.IsShown, telemetry.State.Status));
    }

    [Fact]
    public async Task OkHidesTheNoticeAndLeavesUsageStatisticsOnAsync()
    {
        var telemetry = new FakeTelemetry();
        var notice = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding { IsDone = true });
        notice.Activate();
        await notice.Claiming;

        notice.DismissCommand.Execute(null);

        Assert.Equal((false, TelemetryStatus.On), (notice.IsShown, telemetry.State.Status));
    }

    [Fact]
    public async Task NoNoticeIsShownWhileTheWelcomeTourIsDueAndTheNoticeIsLeftForTheTourAsync()
    {
        var telemetry = new FakeTelemetry();
        var notice = new TelemetryNoticeViewModel(telemetry, new FakeOnboarding());

        notice.Activate();
        await notice.Claiming;

        Assert.False(notice.IsShown);
        Assert.True(await telemetry.ClaimNoticeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoNoticeIsShownWhenNothingIsSentAsync()
    {
        var notice = new TelemetryNoticeViewModel(new FakeTelemetry { State = TelemetryState.NotConfigured }, new FakeOnboarding { IsDone = true });

        notice.Activate();
        await notice.Claiming;

        Assert.False(notice.IsShown);
    }
}
