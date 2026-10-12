using Avala.Components.UI;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

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

    private static void Chart(Window window) => FeatureCamera.ScrollTo(window, "Jobs finished this week");

    private static void Hovered(Window window)
    {
        var chart = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "SvgDrawing");
        window.MouseMove(chart.TranslatePoint(new Point(chart.Bounds.Width / 2, chart.Bounds.Height / 2), window)!.Value);
    }

    private static Action<Window> Grown(TimeSpan elapsed) =>
        window =>
        {
            var clock = new FakeTimeProvider();
            CanvasPresenter.SetClock(window, clock);
            var play = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PlayAnimation");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Tick();
            clock.Advance(elapsed);
            Tick();
        };

    private static void Tick()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
