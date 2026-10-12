using Avala.ClaudeCode;
using Avala.Sdk;
using Avala.Shell;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class WelcomeTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task WithoutAnyConnectionTheWelcomeDisablesItsComposerAndAConnectionAddedInSettingsEnablesItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], developer: false);
        var shell = run.Get<ShellViewModel>();
        var jobs = run.Page("Jobs");
        await run.Ui.RunAsync(() =>
        {
            shell.Activate();

            return jobs["Checking"].Value<Task>();
        });
        var welcome = await run.Ui.ReadAsync(() => jobs["Welcome"]);
        var blocked = await run.Ui.ReadAsync(() => (welcome["CanCompose"].Value<bool>(), welcome["Chips"].Items.Single()["IsAttention"].Value<bool>()));

        await run.Ui.RunAsync(() =>
        {
            shell.SelectedPage = (IPage)run.Page("Settings").Target;

            return run.Page("Settings")["Loading"].Value<Task>();
        });
        var shown = await run.Ui.ReadAsync(() => shell.SelectedPage?.Title);
        await AddConnectionAsync(run, run.Page("Settings")["Machine"]);
        await run.Ui.RunAsync(() =>
        {
            shell.SelectedPage = (IPage)jobs.Target;

            return jobs["Checking"].Value<Task>();
        });
        var after = await run.Ui.ReadAsync(() => welcome["CanCompose"].Value<bool>());
        await run.Ui.InvokeAsync(shell.Deactivate, TestContext.Current.CancellationToken);

        Assert.Equal((false, true), blocked);
        Assert.Equal("Settings", shown);
        Assert.True(after);
    }

    [Theory]
    [InlineData("developer mode", false)]
    [InlineData("Claude Code with an API key", false)]
    [InlineData("Claude Code without a login", true)]
    public async Task TheComposerIsDisabledOnlyWhenNoConnectionCanRunAJobAsync(string machine, bool blocked)
    {
        await using var home = new TemporaryFolder();
        var claudeCode = machine.StartsWith("Claude Code", StringComparison.Ordinal)
            ? Option<IPlugin>.Some(new ClaudeCodePlugin("claude", [], home.Path, machine.EndsWith("API key", StringComparison.Ordinal) ? new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-test" } : new Dictionary<string, string>()))
            : Option<IPlugin>.None;
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], developer: machine == "developer mode", claudeCode: claudeCode);
        var jobs = run.Page("Jobs");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)jobs.Target).Activate();

            return jobs["Checking"].Value<Task>();
        });

        Assert.Equal(!blocked, await run.Ui.ReadAsync(() => jobs["Welcome"]["CanCompose"].Value<bool>()));
        await run.Ui.InvokeAsync(((IActivatable)jobs.Target).Deactivate, TestContext.Current.CancellationToken);
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
