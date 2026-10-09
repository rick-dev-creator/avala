using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Sidebar;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Views;

public sealed class WorkbenchViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePageShowsTheOpenConversationAndItsActionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkbenchViewModel());

            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "ConversationView");
            Assert.Equal((false, false, true), (view.Shows("NoJob"), view.Shows("ReviewSheet"), view.HasClass("ToggleInspector", "selected")));
            Assert.False(view.Find<Button>("OpenReview").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task WithoutASelectedJobThePageSaysHowToChooseOneAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Workbench());

            Assert.True(view.Shows("NoJob"));
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
    public Task AnOpenReviewDimsThePageUnderItsSheetAndTheSheetsCloseButtonReturnsToTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = bench.Workbench();
            var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
            bench.Publish(Bench.OnBoard(job));
            page.Activate();
            bench.Messenger.Send(new JobSelected(job.Job));
            var view = Screen.Show(page);
            view.Window.Height = 1000;

            view.Press(Key.R, RawInputModifiers.Control);
            var opened = (view.Shows("ReviewSheet"), view.Shows("ReviewScrim"), view.Shows("Sheet"));
            view.Click("Close");

            Assert.Equal((true, true, true), opened);
            Assert.Equal((false, (object?)null), (view.Shows("ReviewSheet"), page.Review));
            Assert.False(view.Shows("NoJob"));
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
            Assert.Equal("Fix JPY rounding in invoice totals", content.TextOf("Title"));
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
            Assert.Equal(("2", false), (view.TextOf("Pending"), view.Shows("NoJobs")));
            Assert.Single(view.All<Button>(), button => button.Classes.Contains("selected"));
        }, Cancellation);

    [Fact]
    public Task AnEmptySidebarSaysSoAndHidesEveryGroupAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Sidebar());

            Assert.Equal((true, false, false, false, false, false), (view.Shows("NoJobs"), view.Shows("NeedsYouGroup"), view.Shows("RunningGroup"), view.Shows("ReadyForReviewGroup"), view.Shows("DoneGroup"), view.Shows("PendingBadge")));
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
    public Task ControlDOpensTheDecisionsPopoverAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var sidebar = bench.Sidebar();
            var view = Screen.Show(sidebar);
            var closed = view.Shows("DecisionsPopover");

            view.Press(Key.D, RawInputModifiers.Control);

            Assert.Equal((false, true), (closed, view.Shows("DecisionsPopover")));
            Assert.Contains("Nothing needs you", view.VisibleTexts);
        }, Cancellation);
}

public sealed class JobRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsDotTitleFactAndPendingBadgeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobRowViewModel());

            Assert.Equal(("Add invoice PDF endpoint", "asks a question", "1"), (view.TextOf("Title"), view.TextOf("Fact"), view.TextOf("Pending")));
            Assert.True(view.Shows("Badge"));
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "StatusDotView");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARowWithNothingPendingShowsNoBadgeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobRowViewModel(Presenting.SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "2 of 4", Components.Status.StatusKind.Working, 0));

            Assert.False(view.Shows("Badge"));
        }, TestContext.Current.CancellationToken);
}
