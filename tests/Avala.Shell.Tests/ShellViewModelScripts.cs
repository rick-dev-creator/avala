using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using Avala.Testing;

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

        ViewModelScript.Given(Shell([], [Into(ShellRegions.Sidebar, 20, late), Into(ShellRegions.Sidebar, 10, early), Into(ShellRegions.SidebarFooter, 0, footer)]))
            .Then(shell =>
            {
                Assert.Equal([early, late], shell.Sidebar.Items);
                Assert.Equal([footer], shell.SidebarFooter.Items);
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
    public void SelectingAPageActivatesItAndDeactivatesTheOneItLeaves()
    {
        var jobs = new Page("Jobs");
        var usage = new Page("Usage");

        ViewModelScript.Given(Shell([jobs, usage], []))
            .When(shell => shell.Activate())
            .When(shell => shell.SelectedPage = usage)
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
        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, usage), Into(ShellRegions.Inspector, 1, decisions), Into(ShellRegions.Sidebar, 0, sidebar)], contexts);

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
        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, section)], contexts);
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

        _ = new ShellViewModel([], [Into(ShellRegions.Inspector, 0, section)], contexts);

        Assert.True(section.Focus.IsSome);
    }

    [Fact]
    public void ASelectionInOneRegionShowsInAnother()
    {
        var contexts = new RegionContexts();
        var list = new JobList(contexts);
        var details = new Section();
        _ = new ShellViewModel([], [Into(ShellRegions.Sidebar, 0, list), Into(ShellRegions.Inspector, 0, details)], contexts);

        ViewModelScript.Given(list).When(sidebar => sidebar.Select("job-3"));

        Assert.Equal(new JobInFocus("job-3"), details.Focus.Match(focus => focus, () => new JobInFocus("none")));
    }

    private static ShellViewModel Shell(IEnumerable<IPage> pages, IEnumerable<RegionContribution> contributions) =>
        new(pages, contributions, new RegionContexts());

    private static RegionContribution Into(RegionName region, int order, object viewModel) => new(region, order, viewModel);

    private sealed record JobInFocus(string Id);

    private sealed class Page(string title) : IPage, IActivatable
    {
        public string Title { get; } = title;

        public List<string> Calls { get; } = [];

        public void Activate() => Calls.Add("activated");

        public void Deactivate() => Calls.Add("deactivated");
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
