using Avala.Fleet.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.FleetActivity;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.FleetActivity;

internal sealed class FakeFleetActivity : IFleetActivity
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static readonly FleetCostKind Usd = new("USD", "apiEquivalent");

    public FleetZone Zone { get; set; } = new("Europe/Madrid", InUtc: false);

    public FleetUsageView Usage { get; set; } = new(FleetReadState.Read, Fleet());

    public FleetJobsView Jobs { get; set; } = new(
        FleetReadState.Read,
        [new FleetJob(Guid.NewGuid(), "laptop", Guid.NewGuid(), "needsHelp", "budgetExceeded", 2, "failed", new FleetTokens(500, 50, 0, 0, 0), [], Noon)]);

    public List<FleetUsageQuery> Asked { get; } = [];

    public List<string> AskedJobs { get; } = [];

    public Queue<TaskCompletionSource<FleetUsageView>> Held { get; } = [];

    public static FleetUsage Fleet(int repositories = 2, bool costed = true) =>
        new(
            costed ? Usd : Option<FleetCostKind>.None,
            new FleetTotals(new FleetTokens(3000, 300, 20000, 1000, 50), Costs(costed, 1.25m), 3, 2),
            [Period(0, 0m), Period(1, 0.5m), Period(2, 0.75m)],
            [
                .. Enumerable.Range(0, repositories).Select(index => Share(FleetShareKind.Repository, $"remote:github.com/rick/repo{index}", costed, 1m - (index * 0.05m))),
                Share(FleetShareKind.Unknown, string.Empty, costed, 0.01m),
                Share(FleetShareKind.OutsideAnyJob, string.Empty, costed, 0.005m),
            ],
            [Share(FleetShareKind.Instance, "laptop", costed, 0.75m)],
            [Share(FleetShareKind.Model, "opus", costed, 1.05m)]);

    public ValueTask<FleetUsageView> UsageAsync(FleetUsageQuery query, CancellationToken cancellationToken)
    {
        Asked.Add(query);

        return Held.TryDequeue(out var held) ? new ValueTask<FleetUsageView>(held.Task) : ValueTask.FromResult(Usage);
    }

    public ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken)
    {
        AskedJobs.Add(repository);

        return ValueTask.FromResult(Jobs);
    }

    private static IReadOnlyList<FleetCost> Costs(bool costed, decimal amount) => costed ? [new FleetCost("USD", "apiEquivalent", amount)] : [];

    private static FleetPeriod Period(int day, decimal amount) =>
        new(new DateOnly(2026, 10, 23).AddDays(day), new FleetTokens(amount * 1000, 0, 0, 0, 0), Costs(amount > 0, amount));

    private static FleetShare Share(FleetShareKind kind, string key, bool costed, decimal amount) =>
        new(kind, key, key, new FleetTokens(amount * 1000, 0, 0, 0, 0), Costs(costed, amount), 1);
}

public sealed class FleetActivityViewModelScripts : IDisposable
{
    private readonly FakeFleetActivity activity = new();

    private readonly TestUiDispatcher ui = new();

    private readonly FakeTimeProvider clock = new(FakeFleetActivity.Noon);

    public static TheoryData<int, string, string, string> Ranges => new()
    {
        { 1, "2026-09-26", "2026-10-26", nameof(FleetStep.Day) },
        { 2, "2026-10-01", "2026-10-26", nameof(FleetStep.Day) },
        { 3, "2026-07-27", "2026-10-26", nameof(FleetStep.Week) },
        { 4, "2026-04-27", "2026-10-26", nameof(FleetStep.Week) },
        { 5, "2025-11-01", "2026-11-01", nameof(FleetStep.Month) },
    };

    [Fact]
    public async Task OpeningReadsTheLastSevenDays()
    {
        var page = await OpenedAsync();

        Assert.Equal(
            ("2026-10-19", "2026-10-26", FleetStep.Day, true, true),
            (Iso(activity.Asked[0].From), Iso(activity.Asked[0].To), activity.Asked[0].Step, activity.Asked[0].Repository.IsNone, await ui.ReadAsync(() => page.Range.Options[0].IsSelected)));
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public async Task EachRangeReadsItsDatesAndStep(int option, string from, string to, string step)
    {
        var page = await OpenedAsync();

        page.Range.Options[option].ChooseCommand.Execute(null);
        await page.Loading;

        Assert.Equal((from, to, step), (Iso(activity.Asked[^1].From), Iso(activity.Asked[^1].To), activity.Asked[^1].Step.ToString()));
    }

    [Fact]
    public async Task ACustomRangeOverAYearIsRefused()
    {
        var page = await OpenedAsync();
        page.Range.Options[6].ChooseCommand.Execute(null);
        page.Range.CustomFrom = "2025-01-01";
        page.Range.CustomTo = "2026-10-25";

        page.Range.ApplyCustomCommand.Execute(null);

        Assert.Equal((1, "Choose at most a year"), (activity.Asked.Count, page.Range.Refusal));
    }

    [Fact]
    public async Task AFittingCustomRangeIsRead()
    {
        var page = await OpenedAsync();
        page.Range.Options[6].ChooseCommand.Execute(null);
        page.Range.CustomFrom = "2026-10-01";
        page.Range.CustomTo = "2026-10-10";

        page.Range.ApplyCustomCommand.Execute(null);
        await page.Loading;

        Assert.Equal(("2026-10-01", "2026-10-11"), (Iso(activity.Asked[^1].From), Iso(activity.Asked[^1].To)));
    }

    [Fact]
    public async Task ChoosingARepositoryScopesThePageAndListsItsJobs()
    {
        var page = await OpenedAsync();

        await (await ui.ReadAsync(() => page.Lists[0].Rows[0])).ChooseCommand.ExecuteAsync(null);

        Assert.Equal(
            ("remote:github.com/rick/repo0", "remote:github.com/rick/repo0", true, "laptop"),
            (activity.Asked[^1].Repository.Match(key => key, () => string.Empty), activity.AskedJobs[^1], await ui.ReadAsync(() => page.IsScoped), await ui.ReadAsync(() => page.Jobs.Jobs[0].Instance.Value)));
    }

    [Fact]
    public async Task TheSpecialRowsCannotBeChosen()
    {
        var page = await OpenedAsync();
        var rows = await ui.ReadAsync(() => page.Lists[0].Rows.ToList());

        await rows[^1].ChooseCommand.ExecuteAsync(null);

        Assert.Equal(("True,True,False,False", 1), (string.Join(",", rows.Select(row => row.CanChoose)), activity.Asked.Count));
    }

    [Fact]
    public async Task ClearingTheScopeReadsTheWholeFleet()
    {
        var page = await OpenedAsync();
        await (await ui.ReadAsync(() => page.Lists[0].Rows[0])).ChooseCommand.ExecuteAsync(null);

        await page.ClearScopeCommand.ExecuteAsync(null);

        Assert.Equal((true, false), (activity.Asked[^1].Repository.IsNone, await ui.ReadAsync(() => page.IsScoped)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task TheMetricIsSpendWithCostsAndTokensWithout(bool costed, bool spend)
    {
        activity.Usage = new FleetUsageView(FleetReadState.Read, FakeFleetActivity.Fleet(costed: costed));

        var page = await OpenedAsync();

        Assert.Equal(spend, await ui.ReadAsync(() => page.Trend.ShowsSpend));
    }

    [Fact]
    public async Task TheTilesSayWhatTheServerAnswered()
    {
        var page = await OpenedAsync();

        Assert.Equal(
            "Spend 1.25 USD|Tokens 24.4K|Jobs 3|Instances 2",
            await ui.ReadAsync(() => string.Join("|", page.Tiles.Select(tile => $"{tile.Label} {tile.Value}"))));
    }

    [Fact]
    public async Task AListShowsItsTopEightAndHowManyMore()
    {
        activity.Usage = new FleetUsageView(FleetReadState.Read, FakeFleetActivity.Fleet(repositories: 10));

        var page = await OpenedAsync();

        Assert.Equal((8, "and 4 more"), (await ui.ReadAsync(() => page.Lists[0].Rows.Count), await ui.ReadAsync(() => page.Lists[0].More)));
    }

    [Fact]
    public async Task ANoticeReplacesTheFigures()
    {
        activity.Usage = FleetUsageView.Of(FleetReadState.Paused);

        var page = await OpenedAsync();

        Assert.Equal((true, false), (await ui.ReadAsync(() => page.HasNotice), await ui.ReadAsync(() => page.HasFigures)));
    }

    [Fact]
    public async Task TheDashboardSaysTheCertificateIsNotTrusted()
    {
        activity.Usage = FleetUsageView.Of(FleetReadState.Untrusted);

        var page = await OpenedAsync();

        Assert.Contains("certificate is not trusted", await ui.ReadAsync(() => page.Notice), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyRangeSaysNothingWasSpent()
    {
        activity.Usage = new FleetUsageView(FleetReadState.Read, FleetUsage.Empty);

        var page = await OpenedAsync();

        Assert.True(await ui.ReadAsync(() => page.Trend.IsEmpty));
    }

    [Fact]
    public async Task DatesInUtcAreSaid()
    {
        activity.Zone = new FleetZone("UTC", InUtc: true);

        var page = await OpenedAsync();

        Assert.True(await ui.ReadAsync(() => page.HasZoneNote));
    }

    [Fact]
    public async Task AnOlderLoadNeverOverwritesANewerOne()
    {
        var page = await OpenedAsync();
        var slow = new TaskCompletionSource<FleetUsageView>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.Held.Enqueue(slow);
        page.Range.Options[5].ChooseCommand.Execute(null);
        var year = page.Loading;
        activity.Usage = new FleetUsageView(FleetReadState.Read, FakeFleetActivity.Fleet(repositories: 1));

        page.Range.Options[0].ChooseCommand.Execute(null);
        await page.Loading;
        slow.SetResult(new FleetUsageView(FleetReadState.Read, FakeFleetActivity.Fleet(repositories: 6)));
        await year;

        Assert.Equal(3, await ui.ReadAsync(() => page.Lists[0].Rows.Count));
    }

    [Fact]
    public async Task TheChosenMetricSurvivesAReload()
    {
        var page = await OpenedAsync();
        await ui.InvokeAsync(() => page.Trend.ShowTokensCommand.Execute(null), TestContext.Current.CancellationToken);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.False(await ui.ReadAsync(() => page.Trend.ShowsSpend));
    }

    [Fact]
    public async Task ChoosingTokensRedrawsTheListsByTokens()
    {
        var page = await OpenedAsync();

        await ui.InvokeAsync(() => page.Trend.ShowTokensCommand.Execute(null), TestContext.Current.CancellationToken);

        Assert.Equal("1K", await ui.ReadAsync(() => page.Lists[0].Rows[0].Value));
    }

    public void Dispose() => ui.Dispose();

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<FleetActivityViewModel> OpenedAsync()
    {
        var page = new FleetActivityViewModel(activity, ui, clock);
        page.Activate();
        await page.Loading;

        return page;
    }
}
