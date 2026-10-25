using Avala.Testing.UI;
using Avala.Workbench.FleetActivity;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class FleetActivityViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageListsTheRepositoriesAndTheUnattributedJobsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel());

            Assert.Equal((1, true, false), (view.Find<ItemsControl>("RepositoryList").ItemCount, view.Shows("UnattributedText"), view.Shows("NoticeText")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task RefreshRunsTheCommandAsync() =>
        ui.RunAsync(() =>
        {
            var refreshed = 0;
            var view = Screen.Show(new DesignFleetActivityViewModel
            {
                RefreshCommand = new AsyncRelayCommand(() =>
                {
                    refreshed++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("RefreshButton");

            Assert.Equal(1, refreshed);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ANoticeReplacesTheListAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel { Notice = "This instance is not connected to a fleet server.", Repositories = [], Unattributed = string.Empty });

            Assert.Equal((true, false), (view.Shows("NoticeText"), view.Shows("UnattributedText")));
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetRepositoryRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnExpandedRowListsItsJobsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRepositoryRowViewModel());

            Assert.Equal((true, 1), (view.Shows("JobList"), view.Find<ItemsControl>("JobList").ItemCount));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task PersonalDetailsAreHiddenWhenAskedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRepositoryRowViewModel());
            var hidden = view.TextOf("KeyText");

            Components.UI.Privacy.PersonalDetails.SetAreHidden(view.Window, false);
            view.Settle();

            Assert.NotEqual("remote:github.com/rick/avala", hidden);
            Assert.Equal("remote:github.com/rick/avala", view.TextOf("KeyText"));
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetJobRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AJobRowShowsItsInstanceStateAndSpendingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetJobRowViewModel());
            Components.UI.Privacy.PersonalDetails.SetAreHidden(view.Window, false);
            view.Settle();

            Assert.Equal(("laptop", true), (view.TextOf("InstanceText"), view.TextOf("SpendingText").Contains("USD", StringComparison.Ordinal)));
        }, TestContext.Current.CancellationToken);
}
