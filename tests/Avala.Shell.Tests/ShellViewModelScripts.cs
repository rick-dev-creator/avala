using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using Avala.Testing;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Shell.Tests;

public sealed class ShellViewModelScripts
{
    [Fact]
    public void ShowsEveryContributedPageAndSelectsTheFirst()
    {
        var tasks = new Page("Tasks");
        var review = new Page("Review");

        ViewModelScript.Given(Shell([tasks, review], []))
            .Then(shell =>
            {
                Assert.Equal<IPage>([tasks, review], shell.Pages);
                Assert.Same(tasks, shell.SelectedPage);
                Assert.True(shell.HasNavigation);
            });
    }

    [Fact]
    public void SelectsNothingAndHidesTheSidebarWhenNothingIsContributed() =>
        ViewModelScript.Given(Shell([], []))
            .Then(shell =>
            {
                Assert.Null(shell.SelectedPage);
                Assert.False(shell.HasSidebar);
                Assert.False(shell.Inspector.HasItems);
            });

    [Fact]
    public void FillsEachRegionWithItsContributionsInOrder()
    {
        var late = new Section();
        var early = new Section();
        var footer = new Section();
        var overlay = new Section();

        ViewModelScript.Given(Shell([], [Into(ShellRegions.Sidebar, 20, late), Into(ShellRegions.Sidebar, 10, early), Into(ShellRegions.SidebarFooter, 0, footer), Into(ShellRegions.Overlay, 0, overlay)]))
            .Then(shell =>
            {
                Assert.Equal([early, late], shell.Sidebar.Items);
                Assert.Equal([footer], shell.SidebarFooter.Items);
                Assert.Equal([overlay], shell.Overlay.Items);
                Assert.Empty(shell.Toolbar.Items);
                Assert.True(shell.HasSidebar);
            });
    }

    [Fact]
    public void AddsPagesRegisteredIntoTheContentRegion()
    {
        var jobs = new Page("Jobs");
        var usage = new Page("Usage");

        ViewModelScript.Given(Shell([jobs], [Into(ShellRegions.Content, 0, usage)]))
            .Then(shell => Assert.Equal<IPage>([jobs, usage], shell.Pages));
    }

    [Fact]
    public void RejectsAContentContributionThatIsNotAPage() =>
        Assert.Throws<InvalidOperationException>(() => Shell([], [Into(ShellRegions.Content, 0, new Section())]));

    [Fact]
    public void ActivatesItsRegionsAndTheSelectedPageOnlyOnceActive()
    {
        var page = new Page("Jobs");
        var section = new Section();
        var script = ViewModelScript.Given(Shell([page], [Into(ShellRegions.Inspector, 0, section)]));

        Assert.Empty(page.Calls);

        script.When(shell => shell.Activate())
            .When(shell => shell.Deactivate())
            .Then(_ =>
            {
                Assert.Equal(["activated", "deactivated"], page.Calls);
                Assert.Equal(["activated", "deactivated"], section.Calls);
            });
    }

    [Fact]
    public async Task SelectingAPageActivatesItAndDeactivatesTheOneItLeavesAsync()
    {
        var jobs = new Page("Jobs");
        var usage = new Page("Usage");

        var script = ViewModelScript.Given(Shell([jobs, usage], [])).When(shell => shell.Activate());

        (await script.WhenPresentedAsync(shell => shell.SelectedPage = usage, TestContext.Current.CancellationToken))
            .ThenNotified(nameof(ShellViewModel.SelectedPage))
            .Then(_ =>
            {
                Assert.Equal(["activated", "deactivated"], jobs.Calls);
                Assert.Equal(["activated"], usage.Calls);
            });
    }

    [Fact]
    public void TheRegionContextReachesEverySectionOfTheRegion()
    {
        var contexts = new RegionContexts();
        var usage = new Section();
        var decisions = new Section();
        var sidebar = new Section();
        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, usage), Into(ShellRegions.Inspector, 1, decisions), Into(ShellRegions.Sidebar, 0, sidebar)], contexts, new RememberedLayout());

        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-1"));

        Assert.Equal(new JobInFocus("job-1"), usage.Focus.Match(focus => focus, () => new JobInFocus("none")));
        Assert.Equal(new JobInFocus("job-1"), decisions.Focus.Match(focus => focus, () => new JobInFocus("none")));
        Assert.True(sidebar.Focus.IsNone);
    }

    [Fact]
    public void AContextOfAnotherTypeOrAClearedContextReachesSectionsAsNone()
    {
        var contexts = new RegionContexts();
        var section = new Section();
        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, section)], contexts, new RememberedLayout());
        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-1"));

        contexts.SetContext(ShellRegions.Inspector, "not a job");

        Assert.True(section.Focus.IsNone);
        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-2"));
        contexts.ClearContext(ShellRegions.Inspector);
        Assert.True(section.Focus.IsNone);
    }

    [Fact]
    public void AContextSetBeforeTheShellIsBuiltReachesItsSections()
    {
        var contexts = new RegionContexts();
        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-7"));
        var section = new Section();

        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, section)], contexts, new RememberedLayout());

        Assert.True(section.Focus.IsSome);
    }

    [Fact]
    public async Task ASelectionInOneRegionShowsInAnotherOnceTheShellPresentsItAsync()
    {
        var contexts = new RegionContexts();
        var list = new JobList(contexts);
        var details = new Section();
        var shell = new ShellViewModel([], [Into(ShellRegions.Sidebar, 0, list), Into(ShellRegions.Inspector, 0, details)], contexts, new RememberedLayout());

        await ViewModelScript.Given(shell).WhenPresentedAsync(_ => list.Select("job-3"), TestContext.Current.CancellationToken);

        Assert.Equal(new JobInFocus("job-3"), details.Focus.Match(focus => focus, () => new JobInFocus("none")));
        Assert.Equal(1, shell.Revision);
    }

    [Fact]
    public void TheInspectorShowsOnlyWhileSomethingIsInFocus()
    {
        var contexts = new RegionContexts();
        var script = ViewModelScript.Given(new ShellViewModel([], [Into(ShellRegions.Inspector, 0, new Section())], contexts, new RememberedLayout()));
        var hidden = script.ViewModel.IsInspectorShown;

        script.When(_ => contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-1")))
            .ThenNotified(nameof(ShellViewModel.IsInspectorShown))
            .Then(shell => Assert.Equal((false, true), (hidden, shell.IsInspectorShown)))
            .When(_ => contexts.ClearContext(ShellRegions.Inspector))
            .Then(shell => Assert.False(shell.IsInspectorShown));
    }

    [Fact]
    public void AnInspectorWithoutSectionsStaysHiddenWhateverIsInFocus()
    {
        var contexts = new RegionContexts();
        var shell = new ShellViewModel([], [], contexts, new RememberedLayout());

        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-1"));

        Assert.False(shell.IsInspectorShown);
    }

    [Fact]
    public async Task APageRequestedOverTheMessengerIsSelectedAndActivatedAsync()
    {
        var jobs = new Page("Jobs");
        var usage = new Page("Usage");
        var services = new ServiceCollection().AddShell().AddSingleton<IWindowLayout>(new RememberedLayout()).AddSingleton<IPage>(usage).AddSingleton<IPage>(jobs).BuildServiceProvider();
        var shell = services.GetRequiredService<ShellViewModel>();
        shell.Activate();

        await ViewModelScript.Given(shell).WhenPresentedAsync(
            _ => services.GetRequiredService<IMessenger>().Send(new PageRequested(jobs)),
            TestContext.Current.CancellationToken);

        Assert.Same(jobs, shell.SelectedPage);
        Assert.Equal(["activated", "deactivated"], usage.Calls);
        Assert.Equal(["activated"], jobs.Calls);
    }

    [Fact]
    public void AHiddenPageStaysOutOfTheNavigationButCanStillBeShown()
    {
        var jobs = new HiddenPage("Jobs");
        var newJob = new HiddenPage("New job");
        var usage = new Page("Usage");

        ViewModelScript.Given(Shell([jobs, newJob, usage], []))
            .Then(shell => Assert.Equal<IPage>([usage], shell.NavigationPages))
            .Then(shell => Assert.True(shell.HasNavigation))
            .When(shell => shell.Receive(new PageRequested(newJob)))
            .Then(shell => Assert.Same(newJob, shell.SelectedPage));
    }

    [Fact]
    public void WithOnlyHiddenPagesBesideTheFirstTheNavigationIsHidden() =>
        ViewModelScript.Given(Shell([new HiddenPage("Jobs"), new HiddenPage("New job")], []))
            .Then(shell => Assert.Equal((false, 0), (shell.HasNavigation, shell.NavigationPages.Count)));

    [Fact]
    public void ARequestForAPageTheShellDoesNotHoldChangesNothing()
    {
        var jobs = new Page("Jobs");

        ViewModelScript.Given(Shell([jobs], []))
            .When(shell => shell.Receive(new PageRequested(new Page("Elsewhere"))))
            .Then(shell => Assert.Same(jobs, shell.SelectedPage));
    }

    private static ShellViewModel Shell(IEnumerable<IPage> pages, IEnumerable<RegionContribution> contributions) =>
        new(pages, contributions, new RegionContexts(), new RememberedLayout());

    [Fact]
    public async Task CollapsingTheSidebarHidesItAndRemembersTheChoiceAsync()
    {
        var layout = new RememberedLayout();
        var shell = WithSidebar(layout);

        ViewModelScript.Given(shell)
            .Invoke(nameof(ShellViewModel.ToggleSidebarCommand))
            .ThenNotified(nameof(ShellViewModel.IsSidebarShown))
            .Then(collapsed => Assert.False(collapsed.IsSidebarShown));
        await shell.Remembering;

        Assert.Equal(WindowLayout.Default with { IsSidebarCollapsed = true }, layout.Remembered);
    }

    [Fact]
    public async Task TheSidebarStartsCollapsedAndAsWideAsThePersonLeftItAsync()
    {
        var shell = WithSidebar(new RememberedLayout(new WindowLayout(true, 312)));

        shell.Activate();
        await shell.Restoring;

        Assert.Equal((false, 312d), (shell.IsSidebarShown, shell.SidebarWidth));
    }

    [Fact]
    public void ResizingTheSidebarKeepsItBetweenItsNarrowestAndWidestWidths() =>
        ViewModelScript.Given(WithSidebar(new RememberedLayout()))
            .Invoke(nameof(ShellViewModel.ResizeSidebarCommand), 287.6d)
            .ThenNotified(nameof(ShellViewModel.SidebarWidth))
            .Then(resized => Assert.Equal(288d, resized.SidebarWidth))
            .Invoke(nameof(ShellViewModel.ResizeSidebarCommand), 120d)
            .Then(narrowest => Assert.Equal(WindowLayout.NarrowestSidebar, narrowest.SidebarWidth))
            .Invoke(nameof(ShellViewModel.ResizeSidebarCommand), 900d)
            .Then(widest => Assert.Equal(WindowLayout.WidestSidebar, widest.SidebarWidth));

    [Fact]
    public async Task TheSidebarWidthIsRememberedWhenTheResizeEndsAsync()
    {
        var layout = new RememberedLayout();
        var shell = WithSidebar(layout);

        ViewModelScript.Given(shell)
            .Invoke(nameof(ShellViewModel.ResizeSidebarCommand), 330d)
            .Then(_ => Assert.Equal(WindowLayout.Default, layout.Remembered))
            .Invoke(nameof(ShellViewModel.RememberSidebarCommand));
        await shell.Remembering;

        Assert.Equal(WindowLayout.Default with { SidebarWidth = 330 }, layout.Remembered);
    }

    [Fact]
    public async Task ResettingTheSidebarWidthReturnsToTheDefaultAndRemembersItAsync()
    {
        var layout = new RememberedLayout(new WindowLayout(false, 380));
        var shell = WithSidebar(layout);
        shell.Activate();
        await shell.Restoring;

        ViewModelScript.Given(shell)
            .Invoke(nameof(ShellViewModel.ResetSidebarWidthCommand))
            .Then(reset => Assert.Equal(WindowLayout.DefaultSidebarWidth, reset.SidebarWidth));
        await shell.Remembering;

        Assert.Equal(WindowLayout.Default, layout.Remembered);
    }

    [Fact]
    public void ANarrowWindowCollapsesTheSidebarAndAWiderOneBringsItBack() =>
        ViewModelScript.Given(WithSidebar(new RememberedLayout()))
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth - 1)
            .Then(narrow => Assert.False(narrow.IsSidebarShown))
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth)
            .Then(wide => Assert.True(wide.IsSidebarShown));

    [Fact]
    public void AWiderWindowKeepsTheSidebarThePersonCollapsedHidden() =>
        ViewModelScript.Given(WithSidebar(new RememberedLayout()))
            .Invoke(nameof(ShellViewModel.ToggleSidebarCommand))
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth - 1)
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth + 400)
            .Then(wide => Assert.False(wide.IsSidebarShown));

    [Fact]
    public async Task InANarrowWindowTheToggleShowsTheSidebarUntilTheWindowWidensWithoutChangingTheRememberedChoiceAsync()
    {
        var layout = new RememberedLayout();
        var shell = WithSidebar(layout);

        ViewModelScript.Given(shell)
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth - 200)
            .Invoke(nameof(ShellViewModel.ToggleSidebarCommand))
            .Then(peeking => Assert.True(peeking.IsSidebarShown))
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth + 200)
            .Invoke(nameof(ShellViewModel.FitCommand), ShellViewModel.NarrowWidth - 200)
            .Then(narrowAgain => Assert.False(narrowAgain.IsSidebarShown));
        await shell.Remembering;

        Assert.Equal(WindowLayout.Default, layout.Remembered);
    }

    [Fact]
    public void FocusModeHidesTheSidebarAndTheInspectorTogetherUntilItIsLeft()
    {
        var contexts = new RegionContexts();
        var shell = new ShellViewModel([], [Into(ShellRegions.Sidebar, 0, new Section()), Into(ShellRegions.Inspector, 0, new Section())], contexts, new RememberedLayout());
        contexts.SetContext(ShellRegions.Inspector, new JobInFocus("job-1"));

        ViewModelScript.Given(shell)
            .Invoke(nameof(ShellViewModel.ToggleFocusCommand))
            .ThenNotified(nameof(ShellViewModel.IsSidebarShown), nameof(ShellViewModel.IsInspectorShown))
            .Then(focused => Assert.Equal((true, false, false), (focused.IsFocused, focused.IsSidebarShown, focused.IsInspectorShown)))
            .Invoke(nameof(ShellViewModel.ToggleFocusCommand))
            .Then(left => Assert.Equal((false, true, true), (left.IsFocused, left.IsSidebarShown, left.IsInspectorShown)));
    }

    [Fact]
    public void LeavingFocusModeIsOfferedOnlyWhileFocused() =>
        ViewModelScript.Given(WithSidebar(new RememberedLayout()))
            .Then(unfocused => Assert.False(unfocused.LeaveFocusCommand.CanExecute(null)))
            .Invoke(nameof(ShellViewModel.ToggleFocusCommand))
            .Invoke(nameof(ShellViewModel.LeaveFocusCommand))
            .Then(left => Assert.Equal((false, true), (left.IsFocused, left.IsSidebarShown)));

    [Fact]
    public void ShowingTheSidebarLeavesFocusMode() =>
        ViewModelScript.Given(WithSidebar(new RememberedLayout()))
            .Invoke(nameof(ShellViewModel.ToggleFocusCommand))
            .Invoke(nameof(ShellViewModel.ToggleSidebarCommand))
            .Then(shell => Assert.Equal((false, true), (shell.IsFocused, shell.IsSidebarShown)));

    private static ShellViewModel WithSidebar(RememberedLayout layout) =>
        new([], [Into(ShellRegions.Sidebar, 0, new Section())], new RegionContexts(), layout);

    private static RegionContribution Into(RegionName region, int order, object viewModel) => new(region, order, viewModel);

    private sealed record JobInFocus(string Id);

    private sealed class Page(string title) : IPage, IActivatable
    {
        public string Title { get; } = title;

        public List<string> Calls { get; } = [];

        public void Activate() => Calls.Add("activated");

        public void Deactivate() => Calls.Add("deactivated");
    }

    private sealed record HiddenPage(string Title) : IPage
    {
        public PagePlacement Placement => PagePlacement.Hidden;
    }

    private sealed class Section : IActivatable, IRegionAware<JobInFocus>
    {
        public List<string> Calls { get; } = [];

        public Option<JobInFocus> Focus { get; private set; }

        public void Activate() => Calls.Add("activated");

        public void Deactivate() => Calls.Add("deactivated");

        public void OnRegionContextChanged(Option<JobInFocus> context) => Focus = context;
    }

    private sealed class JobList(IRegions regions)
    {
        public void Select(string job) => regions.SetContext(ShellRegions.Inspector, new JobInFocus(job));
    }
}
