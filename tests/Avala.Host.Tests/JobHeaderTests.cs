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
            [("resources.json", $$"""{ {{LeasedPorts.OneBlock()}} }""")],
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

    [Fact]
    public async Task BesideAnOpenInspectorAtTheNarrowestWindowARunThatCannotStartKeepsTheRunButtonInsideTheHeaderAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank",
            [("resources.json", $$"""{ {{LeasedPorts.OneBlock()}} }""")],
            [("package.json", """{ "name": "orders", "scripts": { "dev": "vite --port $AVALA_PORT" } }""")]);
        _ = await run.SettledAsync();
        var workbench = await run.WorkbenchAsync();
        var other = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: reply] Explain the rounding rules to the team")));
        _ = await run.SettledAsync(other);
        _ = await workbench.InspectAsync(other);
        var script = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "RunScriptViewModel");
        await run.Ui.PresentedAsync(script.Presentation, () => script["IsShown"].Value<bool>(), () => script["Label"].Text);
        var (end, width) = (0d, 0d);

        await FeatureCamera.InWindowAsync(ui, run, ThemeVariant.Dark, async (window, drain) =>
        {
            window.Width = 720;
            window.Height = 480;
            await FeatureCamera.SettleAsync(window, drain);
            await script.ExecuteAsync("ToggleCommand");
            await FeatureCamera.SettleAsync(window, drain);
            var header = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Header" && border.GetVisualAncestors().Any(parent => parent.GetType().Name == "WorkbenchView"));
            var button = window.GetVisualDescendants().OfType<Button>().First(found => found.Name == "Run" && found.IsEffectivelyVisible);
            (end, width) = (Avalonia.VisualExtensions.TranslatePoint(button, new Avalonia.Point(button.Bounds.Width, 0), header)?.X ?? double.MaxValue, header.Bounds.Width);

            if (FeatureCamera.Folder is { } folder)
            {
                Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(Path.Combine(Directory.CreateDirectory(folder).FullName, "r5-run-problem-720-inspector.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        });

        Assert.True(end <= width, $"Run ends at {end:0} in a header {width:0} wide beside '{script["Problem"].Text}'");
    }

    [Fact]
    public async Task AtTheNarrowestWindowTheHeaderKeepsTheJobsStatusInSightAsync()
    {
        var (status, header, title, _) = await NarrowestHeaderAsync("98-header-status-360");

        Assert.True(status.Width > 0 && status.Right <= header.Width && status.Bottom <= header.Height && !status.Intersects(title), $"the status is drawn at {status} beside a title at {title} in a header {header}");
    }

    [Fact]
    public async Task AtTheNarrowestWindowALongTitleTakesASecondLineAndOffersItselfWholeAsync()
    {
        var (_, _, _, title) = await NarrowestHeaderAsync("98-header-title-360");

        Assert.Equal((2, true), (title.Lines, title.Tip is string tip && tip == title.Text && Title.StartsWith(tip.TrimEnd('…'), StringComparison.Ordinal)));
    }

    private const string Title = "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank and keep the old one working";

    private async Task<(Avalonia.Rect Status, Avalonia.Size Header, Avalonia.Rect TitleBox, (int Lines, object? Tip, string? Text) Title)> NarrowestHeaderAsync(string shot)
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, Title, [], []);
        _ = await run.SettledAsync();
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.InspectAsync(run.Job);
        var measured = (Status: default(Avalonia.Rect), Header: default(Avalonia.Size), TitleBox: default(Avalonia.Rect), Title: (Lines: 0, Tip: (object?)null, Text: (string?)null));

        await FeatureCamera.InWindowAsync(ui, run, ThemeVariant.Dark, async (window, drain) =>
        {
            window.Width = 360;
            window.Height = 480;
            await FeatureCamera.SettleAsync(window, drain);
            var header = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Header" && border.GetVisualAncestors().Any(parent => parent.GetType().Name == "WorkbenchView"));
            var pill = header.GetVisualDescendants().OfType<ContentControl>().First(found => found.Name == "Pill");
            var title = header.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "JobTitle");
            var origin = Avalonia.VisualExtensions.TranslatePoint(pill, default, header) ?? new Avalonia.Point(double.MaxValue, double.MaxValue);
            var heading = header.GetVisualDescendants().OfType<StackPanel>().First(found => found.Name == "HeaderTitle");
            var headingAt = Avalonia.VisualExtensions.TranslatePoint(heading, default, header) ?? default;
            measured = (pill.IsEffectivelyVisible ? new Avalonia.Rect(origin, pill.Bounds.Size) : default, header.Bounds.Size, new Avalonia.Rect(headingAt, heading.Bounds.Size), (title.TextLayout.TextLines.Count, ToolTip.GetTip(title), title.Text));

            if (FeatureCamera.Folder is { } folder)
            {
                Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(Path.Combine(Directory.CreateDirectory(folder).FullName, $"{shot}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        });

        return measured;
    }
}
