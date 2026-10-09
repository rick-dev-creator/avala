using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Budgets.Storage;
using Avala.Budgets.Tests.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

    [Fact]
    public async Task CarvesSurviveAReopeningAndCountForTheirChildAfterARestartAsync()
    {
        using var folder = new TemporaryFolder();
        var carve = new BudgetCarve(JobId.New(), JobId.New(), [new Cost(0.40m, "USD"), new Cost(1.5m, "EUR")], 400L, 0.5, Nine);
        var uncapped = new BudgetCarve(JobId.New(), JobId.New(), [], Option<long>.None, 0.25, Nine.AddMinutes(1));
        await using (var first = new SqliteInterventionStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(carve, Cancellation);
            await first.RecordAsync(uncapped, Cancellation);
        }

        await using var second = new SqliteInterventionStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(carve with { Child = JobId.New() }, Cancellation);
        var budgeted = new Budgeted();
        budgeted.Store.EarlierCarves = await second.EarlierCarvesAsync(Cancellation);
        await budgeted.Book.RunAsync(Cancellation);

        Assert.Equal(
            [(carve.Parent, carve.Child, "0.40 USD,1.5 EUR", carve.Tokens, carve.Share, carve.At), (uncapped.Parent, uncapped.Child, "", uncapped.Tokens, uncapped.Share, uncapped.At)],
            budgeted.Store.EarlierCarves.Select(read => (read.Parent, read.Child, string.Join(',', read.Cost.Select(cost => $"{cost.Amount} {cost.Currency}")), read.Tokens, read.Share, read.At)));
        Assert.Equal(carve.Child, Outcomes.Present(budgeted.Book.CarveOf(carve.Child)).Child);
    }

    [Fact]
    public async Task TheCapsOfASessionAreStoredWhenItOpensAndASessionOfAnEarlierRunKeepsThemAfterARestartAsync()
    {
        var caps = new BudgetCaps([new Cost(0.50m, "USD")], 40_000L, 0.9);
        var first = new Budgeted();
        await first.OpenAsync(Option<BudgetCaps>.Some(caps));
        var restarted = new Budgeted();
        restarted.Store.EarlierBudgets = first.Store.Budgets;

        await restarted.Book.RunAsync(Cancellation);

        var stored = Assert.Single(first.Store.Budgets);
        Assert.Equal((first.Session, Budgeted.Connection), (stored.Budget.Session, stored.Connection));
        Assert.Equal(Option<SessionBudget>.Some(stored.Budget), restarted.Book.BudgetOf(first.Session));
    }

    [Fact]
    public async Task SessionBudgetsSurviveAReopeningAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        using var folder = new TemporaryFolder();
        var earlier = Capped(SessionId.New());
        await using (var first = new SqliteInterventionStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(earlier, Cancellation);
        }

        await using var second = new SqliteInterventionStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Capped(SessionId.New()), Cancellation);

        var read = Assert.Single(await second.EarlierBudgetsAsync(Cancellation));
        Assert.Equal(StoredJson.Write(earlier), StoredJson.Write(read));
    }

    [Fact]
    public async Task ABudgetsDatabaseCreatedBeforeMigrationsIsUpgradedInPlaceKeepingItsInterventionsAsync()
    {
        using var folder = new TemporaryFolder();
        var paths = new AvalaPaths(folder.Path);
        var kept = Exceeded(JobId.New(), Nine, Option<BudgetError>.None);
        await using (var legacy = new BudgetsDbContext(paths.Database("budgets")))
        {
            await legacy.GetService<IMigrator>().MigrateAsync(legacy.Database.GetMigrations().First(), Cancellation);
            await legacy.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory", Cancellation);
            await legacy.Interventions.AddAsync(StoredIntervention.Of(kept), Cancellation);
            await legacy.SaveChangesAsync(Cancellation);
        }

        await using var store = new SqliteInterventionStore(paths);
        await store.RecordAsync(Capped(SessionId.New()), Cancellation);

        Assert.Equal([kept], await store.EarlierRunsAsync(Cancellation));
        Assert.Empty(await store.EarlierBudgetsAsync(Cancellation));
    }

    private static BudgetedSession Capped(SessionId session) =>
        new(
            new SessionBudget(session, BudgetFileStatus.Applied, Option<BudgetError>.None, new BudgetCaps([new Cost(0.50m, "USD")], 40_000L, 0.9) { CarvePerChild = 0.5 }, new FileOrigin("4f2a9c1", EditedInWorktree: false)),
            new ConnectionName("work"));

    private static BudgetIntervention Exceeded(JobId job, DateTimeOffset at, Option<BudgetError> error) =>
        new(
            new JobHold(job, SessionId.New(), HoldReason.BudgetExceeded, SessionHalt.Interrupted),
            new BudgetBreach(BudgetMeasure.Cost, "USD", 0.012m, 0.01m, error),
            at);
}
