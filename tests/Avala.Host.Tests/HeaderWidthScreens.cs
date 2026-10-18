using Avala.Jobs.Contracts;
using Avala.Sdk.Appearance;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class HeaderWidthScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task TheJobHeaderAndComposerBesideAnOpenInspectorAtEveryWidthAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank and keep the old one working",
            [],
            [(".avala/budget.json", """{ "holdAtLimit": 0.98 }""")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.InspectAsync(run.Job);
        var usage = run.Get<IEnumerable<RegionContribution>>().Select(contribution => new Bound(contribution.ViewModel)).First(item => item.Kind == "UsageLeftViewModel");
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["IsShown"].Value<bool>() && usage["Windows"].Items.Count == 2, () => "the usage gauge shows no two windows");

        foreach (var (name, width, height, sidebar) in new (string, double, double, double)[] { ("720-sidebar", 720, 480, WindowLayout.NarrowestSidebar), ("720-peek", 720, 480, 0), ("1000", 1000, 700, WindowLayout.DefaultSidebarWidth), ("1280", 1280, 800, WindowLayout.DefaultSidebarWidth), ("1920", 1920, 1080, WindowLayout.WidestSidebar) })
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                await Camera.ShootAsync(run, $"98-inspector-{name}-{variant}", variant, Framed(width, height, sidebar), HoverUsage, Draft("Keep the old importer working beside the new one and add a regression test for the CSV header row"));
                await RestoreAsync(run);
            }
        }

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            await Camera.ShootAsync(run, $"99-usage-by-day-720-default-sidebar-{variant}", variant, Framed(720, 480, WindowLayout.DefaultSidebarWidth), Page("Usage"), ScrollToDays);
            await RestoreAsync(run);
        }
    }

    private static Action<Window> Page(string title) =>
        window => ((ShellViewModel)window.DataContext!).SelectedPage = ((ShellViewModel)window.DataContext).Pages.Single(page => page.Title == title);

    private static void ScrollToDays(Window window)
    {
        foreach (var days in window.GetVisualDescendants().OfType<ItemsControl>().Where(found => found.Name == "Days").Take(1))
        {
            days.GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd();
        }
    }

    private static Action<Window> Framed(double width, double height, double sidebar) =>
        window =>
        {
            window.Width = width;
            window.Height = height;
            var shell = (ShellViewModel)window.DataContext!;

            if (sidebar == 0)
            {
                shell.ToggleSidebarCommand.Execute(null);
            }
            else
            {
                shell.ResizeSidebarCommand.Execute(sidebar);
            }
        };

    private static Action<Window> Draft(string text) =>
        window =>
        {
            foreach (var box in window.GetVisualDescendants().OfType<TextBox>().Where(box => box.Name == "Draft" && box.IsEffectivelyVisible).Take(1))
            {
                box.Text = text;
            }
        };

    private static void HoverUsage(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "Open" && button.IsEffectivelyVisible && button.Bounds.Width > 0 && button.GetVisualAncestors().Any(parent => parent.GetType().Name == "UsageLeftView")).Take(1))
        {
            ToolTip.SetIsOpen(button, true);
        }
    }

    private static Task RestoreAsync(SimulatedRun run) =>
        run.Ui.InvokeAsync(
            () =>
            {
                var shell = run.Get<ShellViewModel>();
                shell.FitCommand.Execute(1440d);
                shell.ResizeSidebarCommand.Execute(WindowLayout.DefaultSidebarWidth);
                shell.SelectedPage = shell.Pages.Single(page => page.Title == "Jobs");
            },
            TestContext.Current.CancellationToken).AsTask();
}
