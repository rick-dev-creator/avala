using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Budgets.Tests.Enforcement;

public sealed class CarveTests
{
    private static readonly Result<Option<BudgetCaps>, BudgetError> Capped =
        Option<BudgetCaps>.Some(new BudgetCaps([new Cost(1.00m, "USD")], 1_000L, Option<double>.None) { CarvePerChild = 0.5 });

    private static readonly TokenUsage Tokens200 = new(100, 100, 0, 0, 0);

    [Fact]
    public async Task AChildIsCarvedItsShareOfWhatItsParentHasLeftAfterItsSpendingAndEarlierCarvesAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SpendAsync(Tokens200, new Cost(0.20m, "USD"));
        var first = JobId.New();
        var second = JobId.New();

        await budgeted.SubmittedAsync(budgeted.Job, first);
        await budgeted.SubmittedAsync(budgeted.Job, second);

        var carved = Outcomes.Present(budgeted.Book.CarveOf(first));
        Assert.Equal((budgeted.Job, first, 0.40m, Option<long>.Some(400), 0.5), (carved.Parent, carved.Child, carved.Cost.Single().Amount, carved.Tokens, carved.Share));
        Assert.Equal((0.20m, Option<long>.Some(200)), (Outcomes.Present(budgeted.Book.CarveOf(second)).Cost.Single().Amount, Outcomes.Present(budgeted.Book.CarveOf(second)).Tokens));
        Assert.Equal([carved, Outcomes.Present(budgeted.Book.CarveOf(second))], budgeted.Store.Carves);
        Assert.Equal(new BudgetCarved(carved), budgeted.Bus.Published.OfType<BudgetCarved>().First());
        Assert.Equal(Option<BudgetCarve>.None, budgeted.Book.CarveOf(budgeted.Job));
    }

    [Fact]
    public async Task AChildSubmittedBeforeItsParentsBudgetIsLoadedIsCarvedFromThatBudgetOnceItIsLoadedAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.TiedAsync();
        var child = JobId.New();

        await budgeted.SubmittedAsync(budgeted.Job, child);
        Assert.Equal(Option<BudgetCarve>.None, budgeted.Book.CarveOf(child));
        Assert.Empty(budgeted.Bus.Published.OfType<BudgetCarved>());
        await budgeted.OpenAsync(Capped);

        var carved = Outcomes.Present(budgeted.Book.CarveOf(child));
        Assert.Equal((budgeted.Job, child, 0.50m, Option<long>.Some(500)), (carved.Parent, carved.Child, carved.Cost.Single().Amount, carved.Tokens));
        Assert.Equal(new BudgetCarved(carved), Assert.Single(budgeted.Bus.Published.OfType<BudgetCarved>()));
    }

    [Fact]
    public async Task AChildWhoseCarveAwaitsItsParentsBudgetIsHeldAgainstThatCarveOnceItIsKnownAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.TiedAsync();
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Capped);

        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(0.60m, "USD"));
        Assert.Empty(budgeted.Jobs.Holds);
        await budgeted.OpenAsync(Capped);

        Assert.Equal([(child, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 0.60m, 0.50m, Option<BudgetError>.None), Assert.Single(budgeted.Book.OfJob(child)).Breach);
    }

    [Fact]
    public async Task AChildWhoseCarveAwaitsItsParentsBudgetIsStillHeldByItsOwnCapsAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.TiedAsync();
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Capped);

        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(1.20m, "USD"));

        Assert.Equal([(child, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 1.20m, 1.00m, Option<BudgetError>.None), Assert.Single(budgeted.Book.OfJob(child)).Breach);
    }

    [Fact]
    public async Task AGrandchildSubmittedWhileItsParentsCarveAwaitsIsCarvedWithinThatCarveAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.TiedAsync();
        var child = JobId.New();
        var grandchild = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Capped);

        await budgeted.SubmittedAsync(child, grandchild);
        Assert.Empty(budgeted.Bus.Published.OfType<BudgetCarved>());
        await budgeted.OpenAsync(Capped);

        Assert.Equal(
            [(budgeted.Job, child, 0.50m), (child, grandchild, 0.25m)],
            budgeted.Bus.Published.OfType<BudgetCarved>().Select(carved => (carved.Carve.Parent, carved.Carve.Child, carved.Carve.Cost.Single().Amount)));
    }

    [Fact]
    public async Task AChildIsHeldWhenItReachesItsCarveEvenBelowItsRepositorysCapAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Capped);

        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(0.49m, "USD"));
        Assert.Empty(budgeted.Jobs.Holds);
        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(0.50m, "USD"));

        Assert.Equal([(child, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 0.50m, 0.50m, Option<BudgetError>.None), Assert.Single(budgeted.Book.OfJob(child)).Breach);
    }

    [Fact]
    public async Task AParentIsHeldWhenItsOwnSpendingAndWhatItCarvedForItsRunningChildrenReachItsCapAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SubmittedAsync(budgeted.Job, JobId.New());

        await budgeted.SpendAsync(new TokenUsage(10, 10, 0, 0, 0), new Cost(0.49m, "USD"));
        Assert.Empty(budgeted.Jobs.Holds);
        await budgeted.SpendAsync(new TokenUsage(10, 10, 0, 0, 0), new Cost(0.50m, "USD"));

        Assert.Equal([(budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 1.00m, 1.00m, Option<BudgetError>.None), Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach);
    }

    [Fact]
    public async Task AChildThatEndedReturnsWhatItDidNotSpendToItsParentAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(0.10m, "USD"));
        await budgeted.ProgressAsync(child, JobStatus.Approved);

        await budgeted.SpendAsync(new TokenUsage(10, 10, 0, 0, 0), new Cost(0.89m, "USD"));
        Assert.Empty(budgeted.Jobs.Holds);
        await budgeted.SpendAsync(new TokenUsage(10, 10, 0, 0, 0), new Cost(0.90m, "USD"));

        Assert.Equal([(budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
    }

    [Fact]
    public async Task AChildThatOvershootsItsCarveHoldsItsParentOnceTheTreeReachesTheParentsCapAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Capped);
        await budgeted.SpendAsync(Tokens200, new Cost(0.30m, "USD"));
        var child = JobId.New();
        await budgeted.SubmittedAsync(budgeted.Job, child);

        await budgeted.SpendAsync(child, new TokenUsage(10, 10, 0, 0, 0), new Cost(0.70m, "USD"));

        Assert.Equal([(budgeted.Job, HoldReason.BudgetExceeded)], budgeted.Jobs.Holds);
        Assert.Equal(1.00m, Assert.Single(budgeted.Book.OfJob(budgeted.Job)).Breach.Measured);
    }

    [Fact]
    public async Task AChildOfAParentWithoutCapsIsCarvedNothingAndIsCappedOnlyByItsOwnBudgetAsync()
    {
        var budgeted = new Budgeted();
        await budgeted.RunningAsync(Option<BudgetCaps>.None);
        var child = JobId.New();

        await budgeted.SubmittedAsync(budgeted.Job, child);
        await budgeted.RunningAsync(child, SessionId.New(), Option<BudgetCaps>.None);
        await budgeted.SpendAsync(child, new TokenUsage(1_000_000, 0, 0, 0, 0), new Cost(100m, "USD"));

        var carved = Outcomes.Present(budgeted.Book.CarveOf(child));
        Assert.Equal((0, Option<long>.None, 0.5), (carved.Cost.Count, carved.Tokens, carved.Share));
        Assert.Empty(budgeted.Jobs.Holds);
    }
}
