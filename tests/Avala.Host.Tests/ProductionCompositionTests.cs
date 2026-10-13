using Avala.ClaudeCode;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ProductionCompositionTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task WithoutTheDevelopmentOnlyPluginsAvalaOffersClaudeCodeAloneAndNoSimulatorAsync()
    {
        await using var home = new TemporaryFolder();
        var missing = Path.Combine(home.Path, "claude");
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [],
            developer: true,
            claudeCode: Option<IPlugin>.Some(new ClaudeCodePlugin(missing, [], home.Path, new Dictionary<string, string>())),
            onboarded: false,
            production: true);
        var shell = run.Get<ShellViewModel>();
        _ = await run.WorkbenchAsync();
        var tour = new Bound(Assert.Single(run.Get<IEnumerable<RegionContribution>>(), contribution => contribution.Region == ShellRegions.Overlay).ViewModel);
        await run.Ui.RunAsync(() => tour["Checking"].Value<Task>());
        await run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)tour["ContinueCommand"].Target).Execute(null);

            return tour["Loading"].Value<Task>();
        });
        var offered = await run.Ui.ReadAsync(() => (
            Agents: tour["Step"]["Agents"].Items.Count,
            Installs: tour["Step"]["Installs"].Items.Select(install => install["Provider"].Text).ToList()));
        var jobs = run.Page("Jobs");
        await run.Ui.RunAsync(() => jobs["Checking"].Value<Task>());
        var chips = await run.Ui.ReadAsync(() => jobs["Welcome"]["Chips"].Items.Select(chip => chip["Label"]["Value"].Text).ToList());
        await run.Ui.RunAsync(() =>
        {
            shell.SelectedPage = (IPage)run.Page("Settings").Target;

            return run.Page("Settings")["Loading"].Value<Task>();
        });
        var editor = await run.Ui.ReadAsync(() => run.Page("Settings")["Machine"]["Editor"]);
        await run.Ui.InvokeAsync(() => editor.Execute("NewCommand"), TestContext.Current.CancellationToken);
        var providers = await run.Ui.ReadAsync(() => editor["Providers"].Items.Select(provider => provider.Text).ToList());
        await run.Ui.InvokeAsync(shell.Deactivate, TestContext.Current.CancellationToken);

        Assert.Equal(0, offered.Agents);
        Assert.Equal(["Claude Code"], offered.Installs);
        Assert.Equal(["No agent"], chips);
        Assert.Equal(["Claude Code · claude-code"], providers);
    }
}
