using System.Text.Json.Nodes;
using Avala.Runtime.Telemetry;
using Avala.Shell;
using Avala.Testing.UI;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class TelemetryTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABuildWithoutAKeySendsNothingAndSettingsSaysSoAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        _ = await run.WorkbenchAsync();

        var page = await SettingsAsync(run);

        Assert.Empty(run.Telemetry.Sent);
        Assert.Equal("Not configured in this build: nothing is ever sent.", await run.Ui.ReadAsync(() => page["About"]["Telemetry"]["Status"].Text));
    }

    [Fact]
    public async Task SettingsShowsExactlyWhatTheRunningAppSentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], telemetry: true);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        _ = await run.WorkbenchAsync();

        var page = await SettingsAsync(run);
        var preview = await run.Ui.ReadAsync(() => page["About"]["Telemetry"]["Preview"].Text);

        Assert.Equal(["installed", "active"], run.Telemetry.Events);
        Assert.Equal(Untimed(run.Telemetry.Sent[^1]), Untimed(preview));
    }

    [Fact]
    public async Task TheNoticeIsShownOnTheFirstRunOnlyAndTurnOffStopsSendingAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], telemetry: true);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        var notice = (await run.WorkbenchAsync()).Footer("TelemetryNoticeViewModel");
        await run.Ui.RunAsync(() => notice["Claiming"].Value<Task>());
        var shown = await run.Ui.ReadAsync(() => notice["IsShown"].Value<bool>());
        await run.Ui.RunAsync(() => notice.ExecuteAsync("TurnOffCommand"));
        var sent = run.Telemetry.Sent.Count;

        await run.RestartAfterAsync(TimeSpan.FromDays(1));
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        var later = (await run.WorkbenchAsync()).Footer("TelemetryNoticeViewModel");
        await run.Ui.RunAsync(() => later["Claiming"].Value<Task>());

        Assert.True(shown);
        Assert.False(await run.Ui.ReadAsync(() => later["IsShown"].Value<bool>()));
        Assert.Equal(sent, run.Telemetry.Sent.Count);
    }

    [Fact]
    public async Task TheFirstRunNoticeAndTheUsageStatisticsSectionAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], telemetry: true);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        var notice = (await run.WorkbenchAsync()).Footer("TelemetryNoticeViewModel");
        await run.Ui.RunAsync(() => notice["Claiming"].Value<Task>());

        await Camera.ShootAsync(run, "telemetry-01-first-run-notice-dark");
        await Camera.ShootAsync(run, "telemetry-01-first-run-notice-light", ThemeVariant.Light);

        var settings = await SettingsAsync(run);
        await run.Ui.InvokeAsync(() => settings.ShowSection("About"), Cancellation);
        await Camera.ShootAsync(run, "telemetry-02-settings-usage-statistics-dark", ShowSection);
        await Camera.ShootAsync(run, "telemetry-02-settings-usage-statistics-light", ThemeVariant.Light, ShowSection);
    }

    private static async Task<Bound> SettingsAsync(SimulatedRun run)
    {
        var page = run.Page("Settings");
        await run.Ui.InvokeAsync(() => run.Get<ShellViewModel>().SelectedPage = (Sdk.IPage)page.Target, Cancellation);
        await run.Ui.RunAsync(() => page["Loading"].Value<Task>());

        return page;
    }

    private static void ShowSection(Avalonia.Controls.Window window) =>
        Avalonia.Controls.ControlExtensions.BringIntoView(
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>().First(block => block.Text == "Usage statistics"),
            new Avalonia.Rect(0, -16, 10, 640));

    private static string Untimed(string payload)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        root.Remove("timestamp");

        return root.ToJsonString();
    }
}
