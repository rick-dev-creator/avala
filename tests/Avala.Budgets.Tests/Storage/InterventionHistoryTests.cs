using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Budgets.Storage;
using Avala.Budgets.Tests.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Budgets.Tests.Storage;

public sealed class InterventionHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInterventionIsStoredAndTheInterventionsOfEarlierRunsCountForTheirJobAsync()
    {
        var budgeted = new Budgeted();
        var earlier = Exceeded(budgeted.Job, Nine.AddDays(-1), Option<BudgetError>.None);
        budgeted.Store.Earlier = [earlier, Exceeded(JobId.New(), Nine, Option<BudgetError>.None)];
        await budgeted.Book.RunAsync(Cancellation);
        await budgeted.RunningAsync(Option<BudgetCaps>.Some(new BudgetCaps([new Cost(0.01m, "USD")], Option<long>.None, Option<double>.None)));

        await budgeted.SpendAsync(new TokenUsage(1, 1, 0, 0, 0), new Cost(0.02m, "USD"));

        var exceeded = Assert.Single(budgeted.Store.Recorded);
        Assert.Equal([earlier, exceeded], budgeted.Book.OfJob(budgeted.Job));
    }

    [Fact]
    public async Task InterventionsSurviveAReopeningAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        using var folder = new TemporaryFolder();
        var earlier = new[] { Exceeded(JobId.New(), Nine, Option<BudgetError>.None), Exceeded(JobId.New(), Nine.AddMinutes(1), BudgetError.InvalidCost) };
        await using (var first = new SqliteInterventionStore(new AvalaPaths(folder.Path)))
        {
            foreach (var intervention in earlier)
            {
                await first.RecordAsync(intervention, Cancellation);
            }
        }

        await using var second = new SqliteInterventionStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Exceeded(JobId.New(), Nine.AddHours(1), Option<BudgetError>.None), Cancellation);

        Assert.Equal(earlier, await second.EarlierRunsAsync(Cancellation));
    }

    private static BudgetIntervention Exceeded(JobId job, DateTimeOffset at, Option<BudgetError> error) =>
        new(
            new JobHold(job, SessionId.New(), HoldReason.BudgetExceeded, SessionHalt.Interrupted),
            new BudgetBreach(BudgetMeasure.Cost, "USD", 0.012m, 0.01m, error),
            at);
}
