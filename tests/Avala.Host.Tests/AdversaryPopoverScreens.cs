using Avala.Jobs.Contracts;
using Avala.Sdk.Appearance;
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

public sealed class AdversaryPopoverScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task EveryOtherPopoverAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, "screens");
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank and keep the old one working",
            [],
            [(".avala/budget.json", """{ "holdAtLimit": 0.98 }""")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);

        foreach (var (size, frame, side) in Sizes)
        {
            await ShootBothAsync(run, $"a1-repository-project-menu-{size}", frame, side, RightClickProject("repository"));
            await ShootBothAsync(run, $"a2-more-menu-{size}", frame, ClickNamed("More"));
            await ShootBothAsync(run, $"a3-selection-menu-{size}", frame, RightClickText("Done, though"));
            await ShootBothAsync(run, $"a4-job-row-menu-keyboard-{size}", frame, side, RightClick(run.Job), Key(Avalonia.Input.Key.Down), Key(Avalonia.Input.Key.Down));
            await ShootBothAsync(run, $"a5-review-tooltip-{size}", frame, side, Tip("OpenReview"));
            await ShootBothAsync(run, $"a6-attach-tooltip-{size}", frame, side, Tip("Attach"));
            await ShootBothAsync(run, $"a7-usage-page-{size}", frame, side, Page("Usage"));
            await ShootBothAsync(run, $"a7b-usage-by-day-{size}", frame, side, Page("Usage"), window => { foreach (var days in window.GetVisualDescendants().OfType<ItemsControl>().Where(found => found.Name == "Days").Take(1)) { days.GetVisualAncestors().OfType<ScrollViewer>().First().ScrollToEnd(); } });
            await ShootBothAsync(run, $"a8-overview-page-{size}", frame, side, Page("Overview"));
            await ShootBothAsync(run, $"a9-overview-card-tooltip-{size}", frame, side, Page("Overview"), TipOfType("ConnectionCardView"));
        }
    }

    private static IEnumerable<(string Size, Action<Window> Frame, Action<Window> Side)> Sizes =>
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

    private static Action<Window> Page(string title) =>
        window => ((ShellViewModel)window.DataContext!).SelectedPage = ((ShellViewModel)window.DataContext).Pages.Single(page => page.Title == title);

    private static Action<Window> RightClick(JobId job) =>
        window => Click(window, window.GetVisualDescendants().OfType<UserControl>().First(row => row.GetType().Name == "JobRowView" && row.DataContext is { } bound && new Bound(bound)["Job"].Value<JobId>() == job), MouseButton.Right);

    private static Action<Window> RightClickProject(string name) =>
        window => Click(window, window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "ProjectName" && text.Text == name), MouseButton.Right);

    private static Action<Window> RightClickText(string text) =>
        window =>
        {
            var block = window.GetVisualDescendants().OfType<Avala.Components.UI.Markdown.MarkdownText>().Last(found => found.IsEffectivelyVisible && text.Length > 0);
            block.SelectAll();
            window.MouseMove(new Point(window.Bounds.Width - 30, block.TranslatePoint(default, window)?.Y ?? 0));
            ((Avalonia.Controls.Primitives.PopupFlyoutBase)block.ContextFlyout!).ShowAt(block, showAtPointer: true);
        };

    private static Action<Window> ClickNamed(string name) =>
        window => Click(window, window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name && control.IsEffectivelyVisible), MouseButton.Left);

    private static Action<Window> Key(Key key) =>
        window =>
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        };

    private static Action<Window> Tip(string name) =>
        window =>
        {
            foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control => control.Name == name && control.IsEffectivelyVisible).Take(1))
            {
                window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? default);
                ToolTip.SetIsOpen(control, true);
            }
        };

    private static Action<Window> TipOfType(string type) =>
        window =>
        {
            foreach (var control in window.GetVisualDescendants().OfType<UserControl>().Where(control => control.GetType().Name == type).Take(1))
            {
                foreach (var tipped in control.GetVisualDescendants().OfType<Control>().Where(found => ToolTip.GetTip(found) is Control).Take(1))
                {
                    window.MouseMove(tipped.TranslatePoint(new Point(tipped.Bounds.Width / 2, tipped.Bounds.Height / 2), window) ?? default);
                    ToolTip.SetIsOpen(tipped, true);
                }
            }
        };

    private static void Click(Window window, Control target, MouseButton button)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width * 0.6, target.Bounds.Height / 2), window) ?? default;
        window.MouseDown(point, button);
        window.MouseUp(point, button);
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
