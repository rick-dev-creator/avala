using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Sidebar;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Views;

public sealed class WorkbenchViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePageHeadsTheConversationWithItsJobPlaceStatusAndInspectorToggleAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkbenchViewModel());

            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "ConversationView");
            Assert.Equal(("Fix JPY rounding in invoice totals", "billing-worker · claude-work"), (view.TextOf("JobTitle"), view.TextOf("Place")));
            Assert.Contains("Running", view.VisibleTexts);
            Assert.Equal((false, false, false, true), (view.Shows("NoJob"), view.Shows("ReviewSheet"), view.HasClass("ToggleInspector", "selected"), view.Shows("ToggleInspector")));
            Assert.False(view.Shows("OpenReview"));
        }, Cancellation);

    [Fact]
    public Task WithoutASelectedJobThePageSaysHowToChooseOneAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Workbench());

            Assert.Equal((true, false), (view.Shows("NoJob"), view.Shows("Header")));
            Assert.Contains("Choose a job in the sidebar to follow its conversation.", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task ControlIOpensTheInspectorOnTheOpenJobAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = bench.Workbench();
            var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
            page.Activate();
            bench.Messenger.Send(new JobSelected(job.Job));
            var view = Screen.Show(page);

            view.Press(Key.I, RawInputModifiers.Control);

            Assert.True(page.IsInspectorOpen);
            Assert.True(view.HasClass("ToggleInspector", "selected"));
            page.Deactivate();
        }, Cancellation);

    [Fact]
    public Task AJobSelectedInTheSidebarOpensItsConversationOnThePageAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = bench.Workbench();
            var sidebar = bench.Sidebar();
            var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
            bench.Publish(Bench.OnBoard(job));
            sidebar.Show(bench.Board.Jobs);
            var bar = Screen.Show(sidebar);
            var content = Screen.Show(page);

            bar.Click("Title");
            content.Settle();

            Assert.False(content.Shows("NoJob"));
            Assert.Equal("Fix JPY rounding in invoice totals", content.TextOf("JobTitle"));
        }, Cancellation);
}

public sealed class SidebarViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task JobsAreGroupedUnderTheirHeadingsWithTheSelectedRowHighlightedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSidebarViewModel());

            Assert.Superset(new HashSet<string>(["Needs you", "Running", "Ready for review", "Done", "Fix JPY rounding in invoice totals", "held: stalled"]), view.VisibleTexts.ToHashSet());
            Assert.False(view.Shows("NoJobs"));
            Assert.Single(view.All<Button>(), button => button.Classes.Contains("selected"));
        }, Cancellation);

    [Fact]
    public Task AnEmptySidebarSaysSoAndHidesEveryGroupAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Sidebar());

            Assert.Equal((true, false, false, false, false), (view.Shows("NoJobs"), view.Shows("NeedsYouGroup"), view.Shows("RunningGroup"), view.Shows("ReadyForReviewGroup"), view.Shows("DoneGroup")));
            Assert.Contains("No jobs yet", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task ClickingARowSelectsItsJobAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var selected = new List<JobId>();
            bench.Messenger.Register<List<JobId>, JobSelected>(selected, (list, message) => list.Add(message.Job));
            var sidebar = bench.Sidebar();
            var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
            sidebar.Show(Bench.Of(Bench.OnBoard(job)));
            var view = Screen.Show(sidebar);

            view.Click("Title");

            Assert.Equal([job.Job], selected);
            Assert.Contains("selected", view.All<Button>().Single(button => button.Classes.Contains("row") && button.DataContext is IJobRowViewModel).Classes);
        }, Cancellation);

    [Fact]
    public Task TheKeyboardSelectsARowAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var selected = new List<JobId>();
            bench.Messenger.Register<List<JobId>, JobSelected>(selected, (list, message) => list.Add(message.Job));
            var sidebar = bench.Sidebar();
            var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
            sidebar.Show(Bench.Of(Bench.OnBoard(job)));
            var view = Screen.Show(sidebar);

            view.All<Button>().Single(button => button.DataContext is IJobRowViewModel).Focus();
            view.Press(Key.Enter);

            Assert.Equal([job.Job], selected);
        }, Cancellation);
}

public sealed class ToolbarViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheToolbarBadgesPendingDecisionsAndOffersANewJobAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignToolbarViewModel());

            Assert.Equal(("3", true, true), (view.TextOf("Pending"), view.Shows("PendingBadge"), view.Shows("NewJob")));
            Assert.Equal(("Pending decisions", "New job"), (AutomationProperties.GetName(view.Find("ToggleDecisions")), AutomationProperties.GetName(view.Find("NewJob"))));
        }, Cancellation);

    [Fact]
    public Task WithNothingPendingTheBadgeIsGoneAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Toolbar());

            Assert.False(view.Shows("PendingBadge"));
        }, Cancellation);

    [Fact]
    public Task ControlDOpensTheDecisionsPopoverAndEscapeClosesItAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var toolbar = bench.Toolbar();
            var view = Screen.Show(toolbar);
            var closed = view.Shows("DecisionsPopover");

            view.Press(Key.D, RawInputModifiers.Control);
            var opened = (view.Shows("DecisionsPopover"), view.HasClass("ToggleDecisions", "selected"));
            view.Find("ToggleDecisions").Focus();
            view.Press(Key.Escape);

            Assert.Equal((false, (true, true)), (closed, opened));
            Assert.False(toolbar.IsDecisionsOpen);
        }, Cancellation);

    [Fact]
    public Task ControlNAsksForANewJobAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var requested = new List<Sdk.IPage>();
            bench.Messenger.Register<List<Sdk.IPage>, Sdk.Presentation.PageRequested>(requested, (list, message) => list.Add(message.Page));
            var view = Screen.Show(bench.Toolbar());

            view.Press(Key.N, RawInputModifiers.Control);

            Assert.Equal([bench.NewJob], requested);
        }, Cancellation);
}

public sealed class JobRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowIsOneLineWithItsDotTitleAndFactAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobRowViewModel());

            Assert.Equal(("Add invoice PDF endpoint", "asks a question"), (view.TextOf("Title"), view.TextOf("Fact")));
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "StatusDotView");
            Assert.Equal(view.Find("Title").Bounds.Center.Y, view.Find("Fact").Bounds.Center.Y, 1);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFinishedJobFadesToTheTertiaryTextAndDropsItsFactAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobRowViewModel(Presenting.SampleJobs.CheckoutSplit, "Paginate audit log export", JobStatus.Approved, "merged", Components.Status.StatusKind.Done, 0));

            Assert.False(view.Shows("Fact"));
            Assert.Equal(Avalonia.Media.Color.Parse("#80808A"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(view.Find<TextBlock>("Title").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALongTitleIsTrimmedRatherThanPushingTheFactOutAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobRowViewModel(Presenting.SampleJobs.JpyRounding, string.Concat(Enumerable.Repeat("Fix JPY rounding in invoice totals ", 8)), JobStatus.Running, "writing a fix", Components.Status.StatusKind.Working, 0));
            view.Window.Width = 340;
            view.Settle();

            Assert.True(view.Shows("Fact"));
            Assert.True(view.Find("Fact").Bounds.Right <= view.Find<TextBlock>("Title").GetVisualParent()!.Bounds.Width);
        }, TestContext.Current.CancellationToken);
}
