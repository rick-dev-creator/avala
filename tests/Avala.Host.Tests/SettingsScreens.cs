using Avala.Shell;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class SettingsScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private static readonly string[] Sections = ["Accounts", "Connections", "Supervision", "Notifications", "Appearance", "About"];

    private static readonly string[] Tabs = ["Permissions", "Checks", "Budget", "Delivery", "Docs", "Files"];

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task EverySectionOfSettingsAndEveryTabOfARepositoryInAWideAndANarrowWindowAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "reply",
            (".avala/permissions.json", """{ "autonomy": "supervised", "rules": [{ "name": "tests", "kind": "command", "target": "npm test", "answer": "allow" }] }"""),
            (".avala/checks.json", """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"], "timeoutSeconds": 300 }, { "name": "lint", "command": "npm", "arguments": ["run", "lint"] }] }"""),
            (".avala/jobs.json", """{ "approval": "merge" }"""));
        _ = await run.SettledAsync();
        _ = await run.WorkbenchAsync();
        var settings = run.Page("Settings");
        await run.Ui.RunAsync(() => settings.OpenRepositoryAsync(run.Repository.Path));

        foreach (var (section, index) in Sections.Select((section, index) => (section, index)))
        {
            await OpenAsync(run, settings, section);
            await ShootAsync(run, $"settings-{index + 1:00}-{section.ToLowerInvariant()}");
        }

        foreach (var (tab, index) in Tabs.Select((tab, index) => (tab, index)))
        {
            await OpenAsync(run, settings, "Repository", tab);
            await ShootAsync(run, $"settings-{index + 7:00}-repository-{tab.ToLowerInvariant()}");
        }

        await OpenAsync(run, settings, "Accounts");
        await ShootAsync(run, "settings-13-narrow-accounts", Narrow);
        await OpenAsync(run, settings, "Repository", "Checks");
        await ShootAsync(run, "settings-14-narrow-repository", Narrow);
    }

    private async Task ShootAsync(SimulatedRun run, string name, params Action<Window>[] arrange)
    {
        await Camera.ShootAsync(run, name + "-dark", ThemeVariant.Dark, arrange);
        await Camera.ShootAsync(run, name + "-light", ThemeVariant.Light, arrange);
    }

    private static ValueTask OpenAsync(SimulatedRun run, Bound settings, string section, string tab = "Permissions") =>
        run.Ui.InvokeAsync(
            () =>
            {
                var shell = run.Get<ShellViewModel>();
                shell.SelectedPage = shell.Pages.Single(page => page.Title == "Settings");
                settings.ShowSection(section);
                settings.ShowTab(tab);
            },
            TestContext.Current.CancellationToken);

    private static void Narrow(Window window) => window.Width = 720;
}
