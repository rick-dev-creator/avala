using Avala.Fleet.Contracts;
using Avala.Testing;
using Avala.Workbench.FleetActivity;

namespace Avala.Workbench.Tests.FleetActivity;

internal sealed class FakeFleetActivity : IFleetActivity
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public FleetView View { get; set; } = new(
        FleetReadState.Read,
        [new FleetRepository("remote:github.com/rick/avala", 3, 2, new FleetTokens(1500, 150, 9000, 0, 0), [new FleetCost("USD", "apiEquivalent", 0.75m)], Noon.AddHours(-1), Noon)],
        1);

    public FleetJobsView Jobs { get; set; } = new(
        FleetReadState.Read,
        [new FleetJob(Guid.NewGuid(), "laptop", Guid.NewGuid(), "needsHelp", "budgetExceeded", 2, "failed", new FleetTokens(500, 50, 0, 0, 0), [], Noon)]);

    public List<string> Asked { get; } = [];

    public ValueTask<FleetView> RepositoriesAsync(CancellationToken cancellationToken)
    {
        Asked.Add("repositories");

        return ValueTask.FromResult(View);
    }

    public ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken)
    {
        Asked.Add($"jobs {repository}");

        return ValueTask.FromResult(Jobs);
    }
}

public sealed class FleetActivityViewModelScripts : IDisposable
{
    private readonly FakeFleetActivity activity = new();

    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task OpeningThePageReadsTheFleetsRepositories()
    {
        var page = new FleetActivityViewModel(activity, ui);

        page.Activate();
        await page.Loading;

        Assert.Equal(
            (1, "3 jobs on 2 instances", false, true),
            (await ui.ReadAsync(() => page.Repositories.Count), (await ui.ReadAsync(() => page.Repositories[0].Summary))[..21], await ui.ReadAsync(() => page.HasNotice), await ui.ReadAsync(() => page.HasUnattributed)));
    }

    [Theory]
    [InlineData(FleetReadState.NotConnected, "Settings › Fleet")]
    [InlineData(FleetReadState.Paused, "paused")]
    [InlineData(FleetReadState.KeyRefused, "refused")]
    [InlineData(FleetReadState.Unreachable, "did not answer")]
    public async Task AFleetThatCannotBeReadSaysWhy(FleetReadState state, string said)
    {
        activity.View = FleetView.Of(state);
        var page = new FleetActivityViewModel(activity, ui);

        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(said, await ui.ReadAsync(() => page.Notice), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshingReadsAgain()
    {
        var page = new FleetActivityViewModel(activity, ui);

        await page.RefreshCommand.ExecuteAsync(null);
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["repositories", "repositories"], activity.Asked);
    }

    public void Dispose() => ui.Dispose();
}

public sealed class FleetRepositoryRowViewModelScripts : IDisposable
{
    private readonly FakeFleetActivity activity = new();

    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task ExpandingARepositoryReadsItsJobsOnEveryInstance()
    {
        var row = new FleetRepositoryRowViewModel(activity.View.Repositories[0], activity, ui);

        await row.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(
            (true, "laptop", "jobs remote:github.com/rick/avala"),
            (await ui.ReadAsync(() => row.IsExpanded), await ui.ReadAsync(() => row.Jobs[0].Instance.Value), activity.Asked[^1]));
    }

    [Fact]
    public async Task ExpandingAgainFolds()
    {
        var row = new FleetRepositoryRowViewModel(activity.View.Repositories[0], activity, ui);

        await row.ToggleCommand.ExecuteAsync(null);
        await row.ToggleCommand.ExecuteAsync(null);

        Assert.False(await ui.ReadAsync(() => row.IsExpanded));
    }

    [Theory]
    [InlineData(0.75, "0.75 USD")]
    [InlineData(0.0042, "0.0042 USD")]
    [InlineData(14.504, "14.5 USD")]
    public void ACostIsShownWithTheDigitsThatMatter(double amount, string shown) =>
        Assert.Contains(
            shown,
            FleetActivityViewModel.Spent(new FleetTokens(0, 0, 0, 0, 0), [new FleetCost("USD", "apiEquivalent", (decimal)amount)]),
            StringComparison.Ordinal);

    [Fact]
    public void OneJobOnOneInstanceIsSaidInTheSingular() =>
        Assert.StartsWith(
            "1 job on 1 instance",
            new FleetRepositoryRowViewModel(activity.View.Repositories[0] with { Jobs = 1, Instances = 1 }, activity, ui).Summary,
            StringComparison.Ordinal);

    [Fact]
    public void ARowSaysWhatItsRepositorySpent() =>
        Assert.Contains("0.75 USD at API prices", new FleetRepositoryRowViewModel(activity.View.Repositories[0], activity, ui).Spending, StringComparison.Ordinal);

    public void Dispose() => ui.Dispose();
}

public sealed class FleetJobRowViewModelScripts
{
    [Fact]
    public void AJobShowsItsStateHoldAndAttempts()
    {
        var row = new FleetJobRowViewModel(new FakeFleetActivity().Jobs.Jobs[0]);

        Assert.StartsWith("needsHelp · held: budgetExceeded · 2 attempts, last failed", row.State, StringComparison.Ordinal);
    }
}
