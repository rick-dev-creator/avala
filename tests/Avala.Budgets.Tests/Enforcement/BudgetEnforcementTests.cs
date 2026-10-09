using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Tests.Enforcement;

public sealed class BudgetEnforcementTests
{
    private static readonly TokenUsage Few = new(10, 10, 0, 0, 0);

    [Fact]
    public async Task ACostCapReachedInItsCurrencyHoldsTheJobAsBudgetExceededAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(cost: [new Cost(0.01m, "USD"), new Cost(1m, "EUR")]));

        await budgeted.SpendAsync(Few, new Cost(0.5m, "EUR"), new Cost(0.012m, "USD"));

        Assert.Equal([(budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        var intervention = Assert.Single(budgeted.Book.OfJob(budgeted.Job));
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 0.012m, 0.01m, Option<BudgetError>.None), intervention.Breach);
        Assert.Equal((HoldReason.BudgetExceeded, budgeted.Clock.GetUtcNow()), (intervention.Hold.Reason, intervention.At));
        Assert.Equal(new BudgetIntervened(intervention), budgeted.Bus.Published[^1]);
        Assert.Empty(budgeted.Book.OfJob(JobId.New()));
    }

    [Fact]
    public async Task ATokenCapCountsEveryTokenTheProviderReportedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(tokens: 1_000));

        await budgeted.SpendAsync(new TokenUsage(400, 300, 200, 50, 50));

        Assert.Equal(
            new BudgetBreach(BudgetMeasure.Tokens, "tokens", 1_000, 1_000, Option<BudgetError>.None),
            Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach);
    }

    [Fact]
    public async Task ALimitWindowOfTheJobsConnectionAtTheThresholdHoldsTheJobAsLimitNearlyReachedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(threshold: 0.9));

        await budgeted.ReachAsync(new UsageLimit("5h", 0.92, Option<DateTimeOffset>.None));

        Assert.Equal([(budgeted.Job, HoldReason.LimitNearlyReached)], budgeted.Jobs.Holds);
        Assert.Equal(
            new BudgetBreach(BudgetMeasure.Limit, "5h", 0.92m, 0.9m, Option<BudgetError>.None),
            Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach);
    }

    [Fact]
    public async Task SpendingBelowEveryCapOfItsConnectionLeavesTheJobRunningWhateverAnotherConnectionReachedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(cost: [new Cost(1m, "USD")], tokens: 1_000, threshold: 0.9));

        await budgeted.SpendAsync(new TokenUsage(400, 300, 200, 50, 49), new Cost(0.99m, "USD"), new Cost(5m, "EUR"));
        await budgeted.ReachAsync(new UsageLimit("5h", 0.89, Option<DateTimeOffset>.None));

        Assert.Empty(budgeted.Jobs.Holds);
    }

    [Fact]
    public async Task ASessionIsBudgetedWithTheCapsOfItsConnectionAsync()
    {
        var budgeted = new Budgeted();

        await budgeted.OpenAsync(Caps(tokens: 100));

        Assert.Equal([Budgeted.Connection], budgeted.ReadFor);
    }

    [Fact]
    public async Task WithoutABudgetFileNothingIsCappedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Option<BudgetCaps>.None);

        await budgeted.SpendAsync(new TokenUsage(long.MaxValue / 8, 0, 0, 0, 0), new Cost(1_000_000m, "USD"));

        Assert.Empty(budgeted.Jobs.Holds);
        Assert.Equal(BudgetFileStatus.Absent, Assert.IsType<BudgetLoaded>(budgeted.Bus.Published[0]).Budget.File);
    }

    [Fact]
    public async Task AnInvalidBudgetFileIsReportedAndHoldsTheJobAsSoonAsItRunsAsync()
    {
        var budgeted = new Budgeted();

        await budgeted.RunningAsync(BudgetError.InvalidCost);

        var loaded = Assert.IsType<BudgetLoaded>(budgeted.Bus.Published[0]).Budget;
        Assert.Equal((BudgetFileStatus.Rejected, BudgetError.InvalidCost), (loaded.File, Outcomes.Present(loaded.Error)));
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin()), loaded.Origin);
        Assert.Equal(Outcomes.Present(budgeted.Book.BudgetOf(budgeted.Session)), loaded);
        Assert.Equal([(budgeted.Job, HoldReason.InvalidBudget)], budgeted.Jobs.Holds);
        Assert.Equal(
            new BudgetBreach(BudgetMeasure.Declaration, ".avala/budget.json", 0, 0, BudgetError.InvalidCost),
            Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach);
    }

    [Fact]
    public async Task ABudgetLoadedAfterItsJobStartedRunningIsEnforcedAsItLoadsAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.TiedAsync();
        Assert.Empty(budgeted.Jobs.Holds);

        await budgeted.OpenAsync(BudgetError.Malformed);

        Assert.Equal([(budgeted.Job, HoldReason.InvalidBudget)], budgeted.Jobs.Holds);
    }

    [Fact]
    public async Task AJobOverBudgetIsHeldOnlyOnceItRunsAgainAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(cost: [new Cost(0.01m, "USD")]));
        await budgeted.ProgressAsync(JobStatus.Checking);

        await budgeted.SpendAsync(Few, new Cost(0.02m, "USD"));
        Assert.Empty(budgeted.Jobs.Holds);

        await budgeted.ProgressAsync(JobStatus.Running);
        Assert.Equal([(budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
    }

    [Fact]
    public async Task AHoldJobsRejectsIsNotRecordedAsAnInterventionAsync()
    {
        var budgeted = new Budgeted(JobRejection.NotRunning);
        await budgeted.RunningAsync(Caps(cost: [new Cost(0.01m, "USD")]));

        await budgeted.SpendAsync(Few, new Cost(0.02m, "USD"));

        Assert.Single(budgeted.Jobs.Holds);
        Assert.Empty(budgeted.Book.OfJob(budgeted.Job));
        Assert.DoesNotContain(budgeted.Bus.Published, published => published is BudgetIntervened);
    }

    [Fact]
    public async Task AJobWhoseProcessesUseItsMemoryCapIsHeldAsMemoryExceededWhenResourcesAreSampledAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Caps(memory: 512));

        await budgeted.MeasureAsync(511L * 1024 * 1024);
        Assert.Empty(budgeted.Jobs.Holds);
        await budgeted.MeasureAsync(600L * 1024 * 1024);

        Assert.Equal([(budgeted.Job, HoldReason.MemoryExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(
            new BudgetBreach(BudgetMeasure.Memory, "megabytes", 600m, 512m, Option<BudgetError>.None),
            Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach);
    }

    private static Result<Option<BudgetCaps>, BudgetError> Caps(Cost[]? cost = null, long tokens = 0, double threshold = 0, long memory = 0) =>
        Option<BudgetCaps>.Some(new BudgetCaps(
            cost ?? [],
            tokens > 0 ? tokens : Option<long>.None,
            threshold > 0 ? threshold : Option<double>.None)
        {
            MemoryPerJobMegabytes = memory > 0 ? memory : Option<long>.None,
        });
}
