using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SvgDrawing = Avalonia.Svg.Skia.Svg;

namespace Avala.Host.Tests;

public sealed class AnimatedCanvasScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task AnAnimatedChartGrowingAndSettledInTheConversationAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.StartAsync(plugins, "animated-canvas");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);

        foreach (var (variant, name) in new[] { (ThemeVariant.Dark, "dark"), (ThemeVariant.Light, "light") })
        {
            await Camera.ShootAsync(run, $"canvas-animated-chart-growing-{name}", variant, Chart, Grown(TimeSpan.FromSeconds(0.7)));
            await Camera.ShootAsync(run, $"canvas-animated-chart-settled-{name}", variant, Chart);
            await Camera.ShootAsync(run, $"canvas-animated-chart-play-on-hover-{name}", variant, Chart, Hovered);
        }
    }

    [Fact]
    public async Task AnAnimatedSolarSystemFillingItsCanvasAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.StartAsync(plugins, "solar-system");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);

        foreach (var (variant, name) in new[] { (ThemeVariant.Dark, "dark"), (ThemeVariant.Light, "light") })
        {
            await Camera.ShootAsync(run, $"canvas-animated-solar-system-{name}", variant, window => FeatureCamera.ScrollTo(window, "Here is the solar system"), Grown(TimeSpan.FromSeconds(6)));
        }
    }

    private static void Chart(Window window) => FeatureCamera.ScrollTo(window, "Jobs finished this week");

    private static void Hovered(Window window)
    {
        var chart = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "SvgDrawing");
        window.MouseMove(chart.TranslatePoint(new Point(chart.Bounds.Width / 2, chart.Bounds.Height / 2), window)!.Value);
    }

    private static Action<Window> Grown(TimeSpan elapsed) =>
        window => window.GetVisualDescendants().OfType<SvgDrawing>().Single().SkSvg!.SetAnimationTime(elapsed);
}
