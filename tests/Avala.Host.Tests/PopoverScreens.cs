using Avala.Jobs.Contracts;
using Avala.Sdk.Appearance;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class PopoverScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string LongProject = "Payments platform migration to the ledger service";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task TheSidebarMenusAndTheUsagePopoverAtTheNarrowestAndWidestWindowAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank and keep the old one working",
            [],
            [(".avala/budget.json", """{ "holdAtLimit": 0.98 }""")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        var other = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: reply] Explain the rounding rules to the team")));
        _ = await run.SettledAsync(other);
        await workbench.ShowsAsync(() => SidebarRows.TopLevel(workbench.Sidebar).Any(row => row["Job"].Value<JobId>() == other));
        await run.Ui.RunAsync(() => SidebarRows.TopLevel(workbench.Sidebar).Single(row => row["Job"].Value<JobId>() == other)["MoveTargets"].Items.Single(target => target["Name"].Text == "New project…").ExecuteAsync("Move"));
        await workbench.ShowsAsync(() => workbench.Sidebar["Projects"].Items.Any(group => group["Name"].Text == "New project"));
        await run.Ui.RunAsync(() =>
        {
            var created = workbench.Sidebar["Projects"].Items.Single(group => group["Name"].Text == "New project");
            created.Set("Draft", LongProject);

            return created.ExecuteAsync("CommitRenameCommand");
        });
        await workbench.ShowsAsync(() => workbench.Sidebar["Projects"].Items.Any(group => group["Name"].Text == LongProject));
        _ = await workbench.SelectAsync(run.Job);
        var usage = Usage(run);
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["IsShown"].Value<bool>() && usage["Windows"].Items.Count == 2, () => "the usage gauge shows no two windows");

        foreach (var (size, frame, sidebar) in Sizes)
        {
            await ShootBothAsync(run, $"90-job-row-menu-{size}", frame, sidebar, RightClick(run.Job));
            await ShootBothAsync(run, $"91-job-row-move-to-{size}", frame, sidebar, RightClick(run.Job), MoveTo);
            await ShootBothAsync(run, $"92-project-menu-{size}", frame, sidebar, RightClickProject(LongProject));
            await ShootBothAsync(run, $"93-usage-popover-{size}", frame, HoverUsage);
            await ShootBothAsync(run, $"94-job-row-menu-keyboard-{size}", frame, sidebar, RightClick(run.Job), Press(Key.Down), Press(Key.Down));
            await ShootBothAsync(run, $"95-more-menu-{size}", frame, LeftClick("More"));
            await ShootBothAsync(run, $"96-usage-page-{size}", frame, sidebar, Page("Usage"));
            await ShootBothAsync(run, $"97-usage-by-day-{size}", frame, sidebar, Page("Usage"), ScrollToDays);
        }
    }

    private static IEnumerable<(string Size, Action<Window> Frame, Action<Window> Sidebar)> Sizes =>
    [
        ("narrowest", Framed(720, 480, WindowLayout.NarrowestSidebar), window => ((ShellViewModel)window.DataContext!).ToggleSidebarCommand.Execute(null)),
        ("widest", Framed(1920, 1080, WindowLayout.WidestSidebar), _ => { }),
    ];

    private static Action<Window> Framed(double width, double height, double sidebar) =>
        window =>
        {
            window.Width = width;
            window.Height = height;
            ((ShellViewModel)window.DataContext!).ResizeSidebarCommand.Execute(sidebar);
        };

    private static Bound Usage(SimulatedRun run) =>
        run.Get<IEnumerable<RegionContribution>>().Select(contribution => new Bound(contribution.ViewModel)).First(item => item.Kind == "UsageLeftViewModel");

    private static Action<Window> RightClick(JobId job) =>
        window => Click(window, window.GetVisualDescendants().OfType<UserControl>().First(row => row.GetType().Name == "JobRowView" && row.DataContext is { } bound && new Bound(bound)["Job"].Value<JobId>() == job));

    private static Action<Window> RightClickProject(string name) =>
        window => Click(window, window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "ProjectName" && text.Text == name));

    private static Action<Window> LeftClick(string name) =>
        window => Click(window, window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name && control.IsEffectivelyVisible), MouseButton.Left);

    private static Action<Window> Press(Key key) =>
        window =>
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        };

    private static Action<Window> Page(string title) =>
        window => ((ShellViewModel)window.DataContext!).SelectedPage = ((ShellViewModel)window.DataContext).Pages.Single(page => page.Title == title);

    private static void ScrollToDays(Window window)
    {
        foreach (var days in window.GetVisualDescendants().OfType<ItemsControl>().Where(found => found.Name == "Days").Take(1))
        {
            days.GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd();
        }
    }

    private static void Click(Window window, Control target, MouseButton button = MouseButton.Right)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width * 0.6, target.Bounds.Height / 2), window) ?? default;
        window.MouseDown(point, button);
        window.MouseUp(point, button);
    }

    private static void MoveTo(Window window)
    {
        foreach (var item in window.GetVisualDescendants().OfType<MenuItem>().Where(item => item.Name == "MoveTo").Take(1))
        {
            item.IsSubMenuOpen = true;
        }
    }

    private static void HoverUsage(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "Open" && button.IsEffectivelyVisible && button.Bounds.Width > 0 && button.GetVisualAncestors().Any(parent => parent.GetType().Name == "UsageLeftView")).Take(1))
        {
            ToolTip.SetIsOpen(button, true);
        }
    }

    private async Task ShootBothAsync(SimulatedRun run, string name, params Action<Window>[] arrange)
    {
        await Camera.ShootAsync(run, name + "-dark", ThemeVariant.Dark, arrange);
        await RestoreAsync(run);
        await Camera.ShootAsync(run, name + "-light", ThemeVariant.Light, arrange);
        await RestoreAsync(run);
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
