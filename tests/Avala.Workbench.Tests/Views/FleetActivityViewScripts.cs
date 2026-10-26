using Avala.Components.Privacy;
using Avala.Testing.UI;
using Avala.Workbench.FleetActivity;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class FleetActivityViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePageShowsItsTilesTrendAndListsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel());

            Assert.Equal(
                (6, true, 3, false, false),
                (view.Find<ItemsControl>("TileList").ItemCount, view.Shows("TrendContent"), view.Find<ItemsControl>("ShareLists").ItemCount, view.Shows("NoticeText"), view.Shows("ScopeChip")));
        }, Cancellation);

    [Fact]
    public Task ANoticeReplacesTheFiguresAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel { Notice = "The fleet is paused in Settings › Fleet.", HasFigures = false });

            Assert.Equal((true, false), (view.Shows("NoticeText"), view.Shows("Figures")));
        }, Cancellation);

    [Fact]
    public Task AScopedPageShowsItsRepositoryAndItsJobsAsync() =>
        ui.RunAsync(() =>
        {
            var cleared = 0;
            var view = Screen.Show(new DesignFleetActivityViewModel
            {
                Scope = Personal.AccountName("remote:github.com/rick/avala"),
                ClearScopeCommand = new AsyncRelayCommand(() =>
                {
                    cleared++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("ClearScopeButton");

            Assert.Equal((true, true, 1), (view.Shows("ScopeChip"), view.Shows("JobsContent"), cleared));
        }, Cancellation);

    [Fact]
    public Task ShowingTheNamesUnmasksThemOnThisPageAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel { Scope = Personal.AccountName("github.com/rick/avala"), AreNamesShown = true });

            Assert.Equal(("github.com/rick/avala", true, "Hide names"), (view.TextOf("ScopeText"), view.Shows("ToggleNamesButton"), view.Find<Button>("ToggleNamesButton").Content));
        }, Cancellation);

    [Fact]
    public Task WhileNamesAreShownEverywhereTheButtonIsNotOfferedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel());

            Components.UI.Privacy.PersonalDetails.SetAreHidden(view.Window, false);
            view.Settle();

            Assert.False(view.Shows("ToggleNamesButton"));
        }, Cancellation);

    [Fact]
    public Task DatesInUtcAreSaidAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetActivityViewModel { ZoneNote = "Dates are in UTC" });

            Assert.True(view.Shows("ZoneNoteText"));
        }, Cancellation);
}

public sealed class FleetRangeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task EveryRangeIsOfferedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRangeViewModel());

            Assert.Equal((7, false), (view.Find<ItemsControl>("RangeOptions").ItemCount, view.Shows("CustomPanel")));
        }, Cancellation);

    [Fact]
    public Task ACustomRangeOffersItsDatesAndSaysWhyItWasRefusedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRangeViewModel { IsCustomOpen = true, Refusal = "Choose at most a year" });

            Assert.Equal((true, true), (view.Shows("CustomPanel"), view.Shows("RefusalText")));
        }, Cancellation);
}

public sealed class FleetRangeOptionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ClickingAnOptionChoosesItAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = 0;
            var view = Screen.Show(new DesignFleetRangeOptionViewModel { ChooseCommand = new RelayCommand(() => chosen++) });

            view.Click("OptionButton");

            Assert.Equal(1, chosen);
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetTileViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ATileShowsItsLabelValueAndCaptionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTileViewModel());

            Assert.Equal(("Spend", "12.40 USD", "at API prices"), (view.TextOf("LabelText"), view.TextOf("ValueText"), view.TextOf("CaptionText")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ATileShowsHowItChangedAndItsSparklineAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTileViewModel());

            Assert.Equal(("▲ 18% vs the previous 7 days", 7), (view.TextOf("ChangeText"), view.Find<ItemsControl>("SparkList").ItemCount));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ATileWithoutAComparisonLeavesOutItsChangeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTileViewModel { Change = string.Empty, Spark = [] });

            Assert.Equal((false, false), (view.Shows("ChangeText"), view.Shows("SparkList")));
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetTrendViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task EveryColumnIsDrawnAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTrendViewModel());

            Assert.Equal((7, false), (view.Find<ItemsControl>("ColumnList").ItemCount, view.Shows("EmptyText")));
        }, Cancellation);

    [Fact]
    public Task AnEmptyRangeSaysNothingWasSpentAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTrendViewModel { IsEmpty = true });

            Assert.Equal((true, false), (view.Shows("EmptyText"), view.Shows("ColumnList")));
        }, Cancellation);

    [Fact]
    public Task TheMetricCanBeSwitchedAsync() =>
        ui.RunAsync(() =>
        {
            var tokens = 0;
            var view = Screen.Show(new DesignFleetTrendViewModel { ShowTokensCommand = new RelayCommand(() => tokens++) });

            view.Click("ShowTokensButton");

            Assert.Equal(1, tokens);
        }, Cancellation);

    [Fact]
    public Task ByTokensTheLegendNamesEachTypeAndThePreviousRangeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetTrendViewModel { ShowsSpend = false });

            Assert.Equal((true, "the previous 7 days"), (view.Shows("Legend"), view.TextOf("PreviousText")));
        }, Cancellation);
}

public sealed class FleetColumnViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task EveryColumnHasItsTooltipAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetColumnViewModel());

            Assert.Equal(
                ("19 Oct", 0.6, "Mon 19 Oct · 2.1M tokens · 2.04 USD at API prices"),
                (view.TextOf("TickText"), view.Find<ProgressBar>("ColumnBar").Value, ToolTip.GetTip(view.Find<Grid>("ColumnCell"))));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AColumnStacksItsTokensAndMarksThePreviousPeriodAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetColumnViewModel());

            Assert.Equal(
                (true, 0.05, true, 0.5),
                (view.Shows("InputLayer"), view.Find<ProgressBar>("InputLayer").Value, view.Shows("PreviousMark"), view.Find<ProgressBar>("PreviousMark").Value));
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetShareListViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AListShowsItsRowsAndHowManyMoreAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetShareListViewModel { More = "and 4 more" });

            Assert.Equal((1, true), (view.Find<ItemsControl>("RowList").ItemCount, view.Shows("MoreText")));
        }, TestContext.Current.CancellationToken);
}

public sealed class FleetShareRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task PersonalDetailsAreHiddenWhenAskedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetShareRowViewModel());
            var hidden = view.TextOf("NameText");

            Components.UI.Privacy.PersonalDetails.SetAreHidden(view.Window, false);
            view.Settle();

            Assert.Equal((false, "remote:github.com/rick/avala"), (hidden == "remote:github.com/rick/avala", view.TextOf("NameText")));
        }, Cancellation);

    [Fact]
    public Task ARowSaysItsPartAndHowItChangedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetShareRowViewModel());

            Assert.Equal("64% · ▲ 12%", view.TextOf("DetailText"));
        }, Cancellation);

    [Fact]
    public Task ChoosingARowRunsItsCommandAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = 0;
            var view = Screen.Show(new DesignFleetShareRowViewModel
            {
                CanChoose = true,
                ChooseCommand = new AsyncRelayCommand(() =>
                {
                    chosen++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("RowButton");

            Assert.Equal(1, chosen);
        }, Cancellation);
}

public sealed class FleetRepositoryJobsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRepositorysJobsAreListedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRepositoryJobsViewModel());

            Assert.Equal((1, true), (view.Find<ItemsControl>("JobList").ItemCount, view.Shows("NoticeText")));
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
