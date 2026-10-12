using Avala.Runtime.Telemetry;
using Avala.Sdk.Regions;

namespace Avala.Host.Tests;

public sealed class OnboardingTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AFreshDataFolderWalksTheTourToTheWelcomeAndTheNextStartShowsNoTourAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], onboarded: false);
        var workbench = await run.WorkbenchAsync();
        var tour = Tour(run);
        await run.Ui.RunAsync(() => tour["Checking"].Value<Task>());
        var opened = await run.Ui.ReadAsync(() => (tour["IsShown"].Value<bool>(), tour["Step"].Kind));

        await run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)tour["ContinueCommand"].Target).Execute(null);

            return tour["Loading"].Value<Task>();
        });
        var agents = await run.Ui.ReadAsync(() => (tour["Step"].Kind, tour["Step"]["Agents"].Items.Count));
        await run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)tour["SkipCommand"].Target).Execute(null);

            return tour["Finishing"].Value<Task>();
        });

        Assert.Equal((true, "WelcomeStepViewModel"), opened);
        Assert.Equal("AgentsStepViewModel", agents.Kind);
        Assert.True(agents.Count > 0);
        Assert.False(await run.Ui.ReadAsync(() => tour["IsShown"].Value<bool>()));
        Assert.False(await run.Ui.ReadAsync(() => workbench.Page.Has("Conversation")));

        await run.RestartAsync();
        _ = await run.WorkbenchAsync();
        var again = Tour(run);
        await run.Ui.RunAsync(() => again["Checking"].Value<Task>());

        Assert.False(await run.Ui.ReadAsync(() => again["IsShown"].Value<bool>()));
    }

    [Fact]
    public async Task TheTourTakesThePlaceOfTheFirstRunTelemetryNoticeAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], telemetry: true, onboarded: false);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        var notice = (await run.WorkbenchAsync()).Footer("TelemetryNoticeViewModel");
        var tour = Tour(run);
        await run.Ui.RunAsync(() => notice["Claiming"].Value<Task>());
        var during = await run.Ui.ReadAsync(() => notice["IsShown"].Value<bool>());
        await run.Ui.RunAsync(() => tour["Checking"].Value<Task>());
        await run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)tour["SkipCommand"].Target).Execute(null);

            return tour["Finishing"].Value<Task>();
        });

        await run.RestartAsync();
        await run.StartedAsync();
        var later = (await run.WorkbenchAsync()).Footer("TelemetryNoticeViewModel");
        await run.Ui.RunAsync(() => later["Claiming"].Value<Task>());

        Assert.False(during);
        Assert.False(await run.Ui.ReadAsync(() => later["IsShown"].Value<bool>()));
    }

    private static Bound Tour(SimulatedRun run) =>
        new(Assert.Single(run.Get<IEnumerable<RegionContribution>>(), contribution => contribution.Region == ShellRegions.Overlay).ViewModel);
}
