using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Budgets.Tests.Enforcement;

public sealed class RaiseTests
{
    private static readonly Result<Option<BudgetCaps>, BudgetError> Capped =
        Option<BudgetCaps>.Some(new BudgetCaps([new Cost(1.00m, "USD")], 1_000L, Option<double>.None) { CarvePerChild = 0.5 });

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobHeldOverItsCostCapIsRaisedByOneAllowanceAboveWhatItCommittedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SpendAsync(new TokenUsage(300, 200, 0, 0, 0), new Cost(1.20m, "USD"));

        var raise = Outcomes.Succeeds(await budgeted.Office.RaiseAsync(budgeted.Job, Cancellation));

        Assert.Equal((budgeted.Job, "1.20 USD", Option<long>.Some(1_000), budgeted.Clock.GetUtcNow(), Option<JobId>.None), Shape(raise));
        Assert.Equal([raise], budgeted.Store.Raises);
        Assert.Equal([raise], budgeted.Office.RaisesOf(budgeted.Job));
        Assert.Equal(new BudgetRaised(raise), budgeted.Bus.Published[^1]);
    }

    [Fact]
    public async Task ARaisedJobIsHeldAgainOnlyOnceItReachesItsRaisedCapAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SpendAsync(new TokenUsage(300, 200, 0, 0, 0), new Cost(1.20m, "USD"));
        _ = Outcomes.Succeeds(await budgeted.Office.RaiseAsync(budgeted.Job, Cancellation));
        await budgeted.ProgressAsync(JobStatus.Running);

        await budgeted.SpendAsync(new TokenUsage(300, 200, 0, 0, 0), new Cost(2.19m, "USD"));
        var within = (budgeted.Jobs.Holds.Count, await budgeted.JudgeTurnAsync());
        await budgeted.SpendAsync(new TokenUsage(300, 200, 0, 0, 0), new Cost(2.20m, "USD"));

        Assert.Equal((1, GateVerdict.Pass), within);
        Assert.Equal(2, budgeted.Jobs.Holds.Count);
        Assert.Equal(GateVerdict.HoldFor(HoldReason.BudgetExceeded), await budgeted.JudgeTurnAsync());
    }

    [Fact]
    public async Task OnlyAJobHeldForItsCostOrTokensCanBeRaisedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Option<BudgetCaps>.Some(new BudgetCaps([new Cost(1.00m, "USD")], Option<long>.None, 0.9)));
        await budgeted.ReachAsync(new UsageLimit("5h", 0.95, Option<DateTimeOffset>.None));

        var limited = await budgeted.Office.RaiseAsync(budgeted.Job, Cancellation);
        var unheld = await budgeted.Office.RaiseAsync(JobId.New(), Cancellation);

        Assert.Equal((BudgetError.NotOverBudget, BudgetError.NotOverBudget), (Outcomes.FailsWith(limited), Outcomes.FailsWith(unheld)));
        Assert.Empty(budgeted.Store.Raises);
        Assert.Empty(budgeted.Bus.Published.OfType<BudgetRaised>());
    }

    [Fact]
    public async Task RaisingAChildRaisesItsCarveAndItsParentByWhatItsReservationGrewAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SpendAsync(new TokenUsage(100, 100, 0, 0, 0), new Cost(0.20m, "USD"));
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Capped);
        await budgeted.SpendAsync(child, new TokenUsage(50, 50, 0, 0, 0), new Cost(0.50m, "USD"));

        var raise = Outcomes.Succeeds(await budgeted.Office.RaiseAsync(child, Cancellation));

        var carve = Outcomes.Present(budgeted.Book.CarveOf(child));
        var parent = Assert.Single(budgeted.Office.RaisesOf(budgeted.Job));
        Assert.Equal((child, "0.50 USD", Option<long>.Some(400)), (Shape(raise).Job, Shape(raise).Cost, Shape(raise).Tokens));
        Assert.Equal((0.90m, Option<long>.Some(800)), (carve.Cost.Single().Amount, carve.Tokens));
        Assert.Equal((budgeted.Job, "0.40 USD", Option<long>.Some(400), budgeted.Clock.GetUtcNow(), Option<JobId>.Some(child)), Shape(parent));
        await budgeted.ProgressAsync(child, JobStatus.Running);
        await budgeted.SpendAsync(child, new TokenUsage(50, 50, 0, 0, 0), new Cost(0.89m, "USD"));
        await budgeted.SpendAsync(new TokenUsage(100, 100, 0, 0, 0), new Cost(0.49m, "USD"));
        Assert.Equal([(child, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        await budgeted.SpendAsync(new TokenUsage(100, 100, 0, 0, 0), new Cost(0.50m, "USD"));
        Assert.Equal([(child, HoldReason.BudgetExceeded), (budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
    }

    [Fact]
    public async Task ARaiseOfAnEarlierRunCountsForItsJobAfterARestartAsync()
    {
        var budgeted = new Budgeted();
        var earlier = new BudgetRaise(budgeted.Job, new SpendCaps([new Cost(1.00m, "USD")], Option<long>.None), budgeted.Clock.GetUtcNow());
        budgeted.Store.EarlierRaises = [earlier, earlier with { Job = JobId.New() }];
        await budgeted.Book.RunAsync(Cancellation);
        await budgeted.RunningAsync(Capped);

        await budgeted.SpendAsync(new TokenUsage(100, 100, 0, 0, 0), new Cost(1.99m, "USD"));

        Assert.Empty(budgeted.Jobs.Holds);
        Assert.Equal([earlier], budgeted.Office.RaisesOf(budgeted.Job));
    }

    private static (JobId Job, string Cost, Option<long> Tokens, DateTimeOffset At, Option<JobId> Child) Shape(BudgetRaise raise) =>
        (raise.Job, string.Join(", ", raise.By.Cost.Select(cost => string.Create(CultureInfo.InvariantCulture, $"{cost.Amount:0.00} {cost.Currency}"))), raise.By.Tokens, raise.At, raise.Child);
}
