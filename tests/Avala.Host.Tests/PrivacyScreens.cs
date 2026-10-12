using Avala.Agents.Contracts.Connections;
using Avala.Components.Privacy;
using Avala.Components.UI.Privacy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Shell;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class PrivacyScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Connections = """
        {
          "default": "claude-work",
          "connections": [
            { "name": "claude-work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "claude-personal", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private static readonly (string File, string Content)[] Accounts =
    [
        ("connections.json", Connections),
        ("connections/claude-work/.login", string.Empty),
        ("connections/claude-personal/.login", string.Empty),
        ("forges.json", """{ "pollSeconds": 300, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task SettingsUsageAndOverviewShowPersonalDetailsMaskedUntilTheyAreRevealedAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.ConnectedAsync(plugins, "markdown", new ConnectionName("claude-work"), Accounts);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        _ = await run.WorkbenchAsync();

        var settings = await OpenAsync(run, "Settings");
        await run.Ui.RunAsync(() => settings["Loading"].Value<Task>());
        var forges = settings["Machine"]["Accounts"]["SourceControl"];
        await run.Ui.UntilAsync(() => forges["Accounts"].Items.Count >= 1, () => $"{forges["Accounts"].Items.Count} source control accounts");
        await ShootBothAsync(run, "90-privacy-settings-accounts-masked");
        var details = run.Get<IPersonalDetailsViewModel>();
        await ShootBothAsync(run, "91-privacy-settings-accounts-revealed", Hidden(details, false), window => PersonalDetails.SetAreHidden(window, details.IsHidden));
        await ShootBothAsync(run, "94-privacy-switch", Hidden(details, true), ShowSwitch);

        var usage = await OpenAsync(run, "Usage");
        await run.Ui.UntilAsync(() => usage["Connections"].Items.Count >= 1, () => $"{usage["Connections"].Items.Count} connections in usage");
        await ShootBothAsync(run, "92-privacy-usage-masked");

        var overview = await OpenAsync(run, "Overview");
        var connections = await run.Ui.ReadAsync(() => overview["Connections"]);
        await run.Ui.PresentedAsync(connections.Presentation, () => connections["Connections"].Items.Count >= 1, () => $"{connections["Connections"].Items.Count} connection cards");
        await ShootBothAsync(run, "93-privacy-overview-masked", HubDetail);

        var masked = await TextsAsync("93-privacy-overview-masked-dark") + await TextsAsync("90-privacy-settings-accounts-masked-dark");
        var revealed = await TextsAsync("91-privacy-settings-accounts-revealed-dark");
        Assert.Contains("S••••• a••••• (c•••••)", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("Simulated account", masked, StringComparison.Ordinal);
        Assert.Contains("av•••••", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("•••••", revealed, StringComparison.Ordinal);
    }

    private static async Task<Bound> OpenAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.InvokeAsync(() => run.Get<ShellViewModel>().SelectedPage = (IPage)page.Target, Cancellation);

        return page;
    }

    private static void ShowSwitch(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Hide personal details").BringIntoView(new Avalonia.Rect(0, -420, 10, 520));

    private static Action<Window> Hidden(IPersonalDetailsViewModel details, bool hidden) =>
        _ =>
        {
            if (details.IsHidden != hidden)
            {
                details.ToggleCommand.Execute(null);
            }
        };

    private static Task<string> TextsAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(FeatureCamera.Folder ?? string.Empty, "texts", name + ".txt"), Cancellation);

    private static void HubDetail(Window window)
    {
        foreach (var core in window.GetVisualDescendants().OfType<Border>().Where(control => control.Name == "Core").Take(1))
        {
            ToolTip.SetIsOpen(core, true);
        }
    }

    private async Task ShootBothAsync(SimulatedRun run, string name, params Action<Window>[] arrange)
    {
        await Camera.ShootAsync(run, name + "-dark", ThemeVariant.Dark, arrange);
        await Camera.ShootAsync(run, name + "-light", ThemeVariant.Light, arrange);
    }
}
