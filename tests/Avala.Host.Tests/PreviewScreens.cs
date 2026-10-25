using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Preview.Contracts;
using Avala.Shell;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class PreviewScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private const string Package = """{ "name": "orders", "scripts": { "dev": "vite --port $AVALA_PORT", "build": "vite build" } }""";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task TheRunningChipTheRunButtonAndThePreviewBesideTheConversationAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("processes"),
            [("resources.json", $$"""{ {{LeasedPorts.Settings()}}, "sampleSeconds": 0.2 }""")],
            [(".avala/permissions.json", ServicesPolicy), ("package.json", Package)]);
        var available = run.Watch<PreviewAvailable>();
        var workbench = await run.WorkbenchAsync();
        await run.StartedAsync();
        var port = ServedPort(await run.TurnAsync());
        await run.DeliveredAsync();
        run.Clock.Advance(TimeSpan.FromSeconds(0.2));
        _ = await available.UntilAsync(seen => seen.Target.Address.Port == port);
        _ = await workbench.SelectAsync(run.Job);
        var regions = workbench.Page["Regions"];
        var chip = regions["Header"].Items.Single(item => item.Kind == "RunningChipViewModel");
        var script = regions["Header"].Items.Single(item => item.Kind == "RunScriptViewModel");
        var pane = regions["Preview"].Items.Single(item => item.Kind == "PreviewPaneViewModel");
        await run.Ui.PresentedAsync(chip.Presentation, () => chip["IsShown"].Value<bool>(), () => chip["Text"].Text);
        await run.Ui.PresentedAsync(script.Presentation, () => script["IsShown"].Value<bool>(), () => script["Label"].Text);

        await Camera.ShootAsync(run, "90-preview-chip-and-run-dark");
        await Camera.ShootAsync(run, "90-preview-chip-and-run-light", ThemeVariant.Light);

        void Open(Avalonia.Controls.Window window) => chip.Execute("OpenCommand");
        void Refuse(Avalonia.Controls.Window window) => run.Surfaces.Opened[0].Navigate(new Uri("https://unpkg.com/three@0.170.0/"));

        await Camera.ShootAsync(run, "91-preview-pane-dark", Open);
        await Camera.ShootAsync(run, "91-preview-pane-light", ThemeVariant.Light, Open);
        await Camera.ShootAsync(run, "92-preview-refused-navigation-dark", Open, Refuse);
        await Camera.ShootAsync(run, "92-preview-refused-navigation-light", ThemeVariant.Light, Open, Refuse);

        void Menu(Avalonia.Controls.Window window)
        {
            var device = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(button => button.Name == "Device");
            device.Flyout!.ShowAt(device);
        }

        void Phone(Avalonia.Controls.Window window) =>
            pane["Frame"]["Choices"].Items.Single(choice => choice["Name"].Text == "iPhone 16").Execute("Choose");

        void Turned(Avalonia.Controls.Window window) => pane["Frame"].Execute("TurnCommand");

        await Camera.ShootAsync(run, "93-preview-device-menu-dark", Open, Menu);
        await Camera.ShootAsync(run, "94-preview-device-iphone-dark", Open, Phone);
        await Camera.ShootAsync(run, "94-preview-device-iphone-light", ThemeVariant.Light, Open);
        await Camera.ShootAsync(run, "95-preview-device-turned-dark", Open, Turned);

        void Classic(Avalonia.Controls.Window window) =>
            pane["Frame"]["Choices"].Items.Single(choice => choice["Name"].Text == "iPhone SE").Execute("Choose");

        void Dual(Avalonia.Controls.Window window) =>
            pane["Frame"]["Choices"].Items.Single(choice => choice["Name"].Text == "iPhone Duo").Execute("Choose");

        await Camera.ShootAsync(run, "96-preview-device-iphone-se-light", ThemeVariant.Light, Open, Classic);
        await Camera.ShootAsync(run, "97-preview-device-iphone-duo-dark", Open, Dual);

        Assert.True(await run.Ui.ReadAsync(() => pane["HasRefusal"].Value<bool>()));
        await run.Ui.InvokeAsync(run.Get<ShellViewModel>().Deactivate, TestContext.Current.CancellationToken);
    }

    private static int ServedPort(IReadOnlyList<IAgentEvent> turn) =>
        int.Parse(
            Assert.Single(turn.OfType<ItemProgressed>(), progressed => progressed.Item.Value == "serve").Text["listening ".Length..],
            CultureInfo.InvariantCulture);
}
