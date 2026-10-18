using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class JobHeaderTests(HeadlessUi ui, PublishedPlugins plugins)
{
    [Fact]
    public async Task ARunThatCannotStartKeepsTheJobsTitleInSightInACompactHeaderAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "[simulate: reply] Rewrite the importer for the new CSV export format of the bank",
            [("resources.json", $$"""{ {{LeasedPorts.OneBlock(test: 4)}} }""")],
            [("package.json", """{ "name": "orders", "scripts": { "dev": "vite --port $AVALA_PORT" } }""")]);
        _ = await run.SettledAsync();
        var workbench = await run.WorkbenchAsync();
        var other = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: reply] Explain the rounding rules to the team")));
        _ = await run.SettledAsync(other);
        _ = await workbench.SelectAsync(other);
        var script = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "RunScriptViewModel");
        await run.Ui.PresentedAsync(script.Presentation, () => script["IsShown"].Value<bool>(), () => script["Label"].Text);
        var title = 0d;

        await FeatureCamera.InWindowAsync(ui, run, ThemeVariant.Dark, async (window, drain) =>
        {
            window.Width = 1000;
            window.Height = 700;
            await FeatureCamera.SettleAsync(window, drain);
            await script.ExecuteAsync("ToggleCommand");
            await FeatureCamera.SettleAsync(window, drain);
            title = window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "JobTitle" && text.IsEffectivelyVisible).Bounds.Width;
        });

        Assert.True(title >= 120, $"the job's title has {title:0} beside '{script["Problem"].Text}'");
    }
}
