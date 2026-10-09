using Avala.Sdk;
using Avala.Shell;

namespace Avala.Host.Tests;

public sealed class FirstRunTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task WithoutAnyConnectionTheJobsPageGuidesToSettingsAndAConnectionAddedThereEndsTheGuideAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], developer: false);
        var shell = run.Get<ShellViewModel>();
        var jobs = run.Page("Jobs");
        await run.Ui.RunAsync(() =>
        {
            shell.Activate();

            return jobs["Checking"].Value<Task>();
        });
        var firstRun = await run.Ui.ReadAsync(() => jobs["FirstRun"]);
        var guide = await run.Ui.ReadAsync(() => (firstRun["IsShown"].Value<bool>(), firstRun["Heading"].Text));

        await run.Ui.InvokeAsync(() => firstRun.Execute("OpenSettingsCommand"), TestContext.Current.CancellationToken);
        await run.Ui.RunAsync(() => run.Page("Settings")["Loading"].Value<Task>());
        var shown = await run.Ui.ReadAsync(() => shell.SelectedPage?.Title);
        await AddConnectionAsync(run, run.Page("Settings")["Machine"]);
        await run.Ui.RunAsync(() =>
        {
            shell.SelectedPage = (IPage)jobs.Target;

            return jobs["Checking"].Value<Task>();
        });
        var after = await run.Ui.ReadAsync(() => firstRun["IsShown"].Value<bool>());
        await run.Ui.InvokeAsync(shell.Deactivate, TestContext.Current.CancellationToken);

        Assert.Equal((true, "No connections yet"), guide);
        Assert.Equal("Settings", shown);
        Assert.False(after);
    }

    private static async Task AddConnectionAsync(SimulatedRun run, Bound machine)
    {
        var editor = await run.Ui.ReadAsync(() => machine["Editor"]);
        await run.Ui.InvokeAsync(() => editor.Execute("NewCommand"), TestContext.Current.CancellationToken);
        await run.Ui.RunAsync(() =>
        {
            editor.Set("Name", "simulator-team");
            editor.Set("Provider", editor["Providers"].Items.Select(provider => provider.Text).ToList().FindIndex(provider => provider.EndsWith("· simulator", StringComparison.Ordinal)));

            return editor.ExecuteAsync("SaveCommand");
        });
    }
}
