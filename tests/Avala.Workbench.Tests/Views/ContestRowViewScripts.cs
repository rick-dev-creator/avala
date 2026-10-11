using Avala.Components.Status;
using Avala.Testing.UI;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ContestRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AContestRowShowsItsTitleItsCandidatesAndThoseThatPassedWithAChevronToShowThemAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignContestRowViewModel());

            Assert.Equal(("Fix JPY rounding in invoice totals", "3 candidates · 2 passed"), (view.TextOf("Title"), view.TextOf("Fact")));
            Assert.Equal((true, false, false), (view.Shows("Collapsed"), view.Shows("Expanded"), view.Shows("PendingBadge")));
        }, Cancellation);

    [Fact]
    public Task ClickingTheChevronRunsTheToggleAsync() =>
        ui.RunAsync(() =>
        {
            var toggled = 0;
            var view = Screen.Show(new DesignContestRowViewModel(SampleJobs.JpyRounding, "Fix JPY rounding", "2 candidates · 1 passed", StatusKind.Working) { ToggleCommand = new RelayCommand(() => toggled++) });

            view.Click("Toggle");

            Assert.Equal(1, toggled);
        }, Cancellation);

    [Fact]
    public Task AnExpandedRowShowsTheChevronPointingDownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignContestRowViewModel() { IsExpanded = true });

            Assert.Equal((false, true), (view.Shows("Collapsed"), view.Shows("Expanded")));
        }, Cancellation);
}
