using Avala.ClaudeCode;
using Avala.Runtime.Telemetry;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class OnboardingScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string GhStatus = """
        github.com
          ✓ Logged in to github.com account octocat (keyring)
          - Active account: true
        """;

    private const string Connections = """
        {
          "default": "auto",
          "connections": [
            { "name": "claude-work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "claude-personal", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "claude-old", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private static readonly (string File, string Content)[] Machine =
    [
        ("connections.json", Connections),
        ("connections/claude-work/.login", string.Empty),
        ("connections/claude-personal/.login", string.Empty),
        ("forges.json", """{ "pollSeconds": 300, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static readonly Dictionary<string, ProcessOutcome> SignedIn = new(StringComparer.Ordinal) { ["gh"] = new ProcessOutcome(0, GhStatus, string.Empty) };

    private static readonly string[] Steps = ["welcome", "agents", "code", "privacy"];

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task EveryStepOfTheTourAtTheFullWindowAndAtTheOwnersWidthAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, Machine, [], telemetry: true, signIns: SignedIn, onboarded: false);
        await run.StartedAsync();
        await run.Get<UsageTelemetry>().Sending;
        _ = await run.WorkbenchAsync();
        var tour = await OpenedAsync(run);

        for (var step = 0; step < Steps.Length; step++)
        {
            await ShootAllAsync(run, $"onboarding-{step + 1}-{Steps[step]}");
            await NextAsync(run, tour);
        }
    }

    [Fact]
    public async Task TheAgentsStepWithoutAnyAgentAsync()
    {
        Gated();
        await using var home = new TemporaryFolder();
        var claudeCode = Option<IPlugin>.Some(new ClaudeCodePlugin("claude", [], home.Path, new Dictionary<string, string>()));
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], developer: false, claudeCode: claudeCode, onboarded: false);
        _ = await run.WorkbenchAsync();
        var tour = await OpenedAsync(run);
        await NextAsync(run, tour);

        await ShootAllAsync(run, "onboarding-2-agents-none");
    }

    private static async Task<Bound> OpenedAsync(SimulatedRun run)
    {
        var tour = new Bound(Assert.Single(run.Get<IEnumerable<RegionContribution>>(), contribution => contribution.Region == ShellRegions.Overlay).ViewModel);
        await run.Ui.RunAsync(() => tour["Checking"].Value<Task>());

        return tour;
    }

    private static Task NextAsync(SimulatedRun run, Bound tour) =>
        run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)tour["ContinueCommand"].Target).Execute(null);

            return tour["Loading"].Value<Task>();
        });

    private async Task ShootAllAsync(SimulatedRun run, string name)
    {
        await Camera.ShootAsync(run, $"{name}-dark");
        await Camera.ShootAsync(run, $"{name}-light", ThemeVariant.Light);
        await Camera.ShootAsync(run, $"{name}-980-dark", OwnersWindow);
        await Camera.ShootAsync(run, $"{name}-980-light", ThemeVariant.Light, OwnersWindow);
    }

    private static void OwnersWindow(Window window)
    {
        window.Width = 980;
        window.Height = 720;
    }

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
}
