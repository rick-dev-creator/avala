using Avala.Components.UI.Theme;
using Avala.Jobs.Contracts;
using Avala.Sdk.Appearance;
using Avala.Shell;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class NavigationScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task TheWindowControlsTheCollapsedSidebarFocusModeAndTheWideReadingColumnAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.StartAsync(plugins, "markdown");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.InspectAsync(run.Job, "AutonomySectionViewModel");

        await ShootBothAsync(run, "70-window-controls");
        await ShootBothAsync(run, "71-sidebar-collapsed", Toggled(shell => shell.ToggleSidebarCommand));
        await ShootBothAsync(run, "72-reading-wide-1920", Wide);
        await ShootBothAsync(run, "73-focus-mode", Toggled(shell => shell.ToggleFocusCommand));
        await ShootBothAsync(run, "74-focus-mode-wide-1920", Wide, Toggled(shell => shell.ToggleFocusCommand));
        await ShootBothAsync(run, "75-narrow-window", window => window.Width = 960);
        await ShootBothAsync(run, "76-settings-sidebar-collapsed", Page("Settings"), Toggled(shell => shell.ToggleSidebarCommand));
        await ShootBothAsync(run, "77-overview-sidebar-collapsed", Page("Overview"), Toggled(shell => shell.ToggleSidebarCommand));
        await ShootBothAsync(run, "78-sidebar-narrowest", Sidebar(WindowLayout.NarrowestSidebar));
        await ShootBothAsync(run, "79-sidebar-widest", Sidebar(WindowLayout.WidestSidebar));
        await ShootBothAsync(run, "80-settings", Page("Settings"));
        await ShootBothAsync(run, "81-settings-sidebar-narrowest", Page("Settings"), Sidebar(WindowLayout.NarrowestSidebar));
        await ShootBothAsync(run, "82-usage-sidebar-narrowest", Page("Usage"), Sidebar(WindowLayout.NarrowestSidebar));
    }

    private static Action<Window> Page(string title) =>
        window => ((ShellViewModel)window.DataContext!).SelectedPage = ((ShellViewModel)window.DataContext).Pages.Single(page => page.Title == title);

    private static Action<Window> Sidebar(double width) =>
        window => ((ShellViewModel)window.DataContext!).ResizeSidebarCommand.Execute(width);

    private static void Wide(Window window)
    {
        window.Width = 1920;
        window.Height = 1080;
        Reading.SetIsWide(window, true);
    }

    private static Action<Window> Toggled(Func<ShellViewModel, CommunityToolkit.Mvvm.Input.IRelayCommand> command) =>
        window => command((ShellViewModel)window.DataContext!).Execute(null);

    private async Task ShootBothAsync(SimulatedRun run, string name, params Action<Window>[] arrange)
    {
        await Camera.ShootAsync(run, name + "-dark", ThemeVariant.Dark, arrange);
        await UndoAsync(run, arrange);
        await Camera.ShootAsync(run, name + "-light", ThemeVariant.Light, arrange);
        await UndoAsync(run, arrange);
    }

    private static Task UndoAsync(SimulatedRun run, Action<Window>[] arrange) =>
        arrange.Length == 0 ? Task.CompletedTask : run.Ui.InvokeAsync(() => Restore(run.Get<ShellViewModel>()), TestContext.Current.CancellationToken).AsTask();

    private static void Restore(ShellViewModel shell)
    {
        if (shell.IsFocused)
        {
            shell.ToggleFocusCommand.Execute(null);
        }

        if (shell.IsSidebarCollapsed)
        {
            shell.ToggleSidebarCommand.Execute(null);
        }

        shell.FitCommand.Execute(1440d);
        shell.ResizeSidebarCommand.Execute(WindowLayout.DefaultSidebarWidth);
        shell.SelectedPage = shell.Pages.Single(page => page.Title == "Jobs");
    }
}
