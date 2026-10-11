using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Budgets.Tests.Enforcement;

public sealed class CeilingTests
{
    private static readonly ConnectionName Work = new("work");
    private static readonly ConnectionName TeamApi = new("team-api");
    private static readonly DateOnly Today = new(2026, 10, 12);

    [Fact]
    public async Task AMachineCeilingReachedTodayHoldsEveryRunningJobAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { SpendPerDay = [new Cost(40m, "USD")] });
        var first = await ceilinged.RunningAsync(Work);
        var second = await ceilinged.RunningAsync(TeamApi);

        await ceilinged.SpentAsync(Today, 40m, (Work, 30m), (TeamApi, 10m));

        Assert.Equal([(first, HoldReason.BudgetExceeded), (second, HoldReason.BudgetExceeded)], ceilinged.Jobs.Holds);
        var intervention = Assert.Single(ceilinged.Book.OfJob(first));
        Assert.Equal(new BudgetBreach(BudgetMeasure.DailySpend, "USD", 40m, 40m, Option<BudgetError>.None), intervention.Breach);
        Assert.Contains(new BudgetIntervened(intervention), ceilinged.Bus.Published);
    }

    [Fact]
    public async Task AConnectionCeilingHoldsOnlyTheJobsOnThatConnectionAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { Connections = [new ConnectionCeiling(TeamApi, [new Cost(10m, "USD")])] });
        _ = await ceilinged.RunningAsync(Work);
        var capped = await ceilinged.RunningAsync(TeamApi);

        await ceilinged.SpentAsync(Today, 35m, (Work, 25m), (TeamApi, 10m));

        Assert.Equal([(capped, HoldReason.BudgetExceeded)], ceilinged.Jobs.Holds);
        Assert.Equal(
            new BudgetBreach(BudgetMeasure.DailySpend, "USD", 10m, 10m, Option<BudgetError>.None) { Connection = TeamApi },
            Assert.Single(ceilinged.Book.OfJob(capped)).Breach);
    }

    [Fact]
    public async Task SpendingOfAnEarlierLocalDayHoldsNothingTodayAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { SpendPerDay = [new Cost(40m, "USD")] });
        _ = await ceilinged.RunningAsync(Work);

        await ceilinged.SpentAsync(Today.AddDays(-1), 50m, (Work, 50m));

        Assert.Empty(ceilinged.Jobs.Holds);
        Assert.Equal([Today], ceilinged.History.Asked.Distinct());
    }

    [Fact]
    public async Task AJobCheckingOrAwaitingReviewIsNotHeldByTheCeilingAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { SpendPerDay = [new Cost(40m, "USD")] });
        var checking = await ceilinged.RunningAsync(Work);
        var reviewed = await ceilinged.RunningAsync(Work);
        await ceilinged.ProgressAsync(checking, JobStatus.Checking);
        await ceilinged.ProgressAsync(reviewed, JobStatus.AwaitingReview);

        await ceilinged.SpentAsync(Today, 40m, (Work, 40m));

        Assert.Empty(ceilinged.Jobs.Holds);
    }

    [Fact]
    public async Task AJobThatRunsOnceTheCeilingWasReachedIsHeldAsItRunsAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { SpendPerDay = [new Cost(40m, "USD")] });
        var reviewed = await ceilinged.RunningAsync(Work);
        await ceilinged.ProgressAsync(reviewed, JobStatus.AwaitingReview);
        await ceilinged.SpentAsync(Today, 40m, (Work, 40m));

        var started = await ceilinged.RunningAsync(Work);
        await ceilinged.ProgressAsync(reviewed, JobStatus.Running);

        Assert.Equal([(started, HoldReason.BudgetExceeded), (reviewed, HoldReason.BudgetExceeded)], ceilinged.Jobs.Holds);
    }

    [Fact]
    public async Task ARejectedMachineBudgetHoldsEveryJobAsAnInvalidBudgetAsSoonAsItRunsAsync()
    {
        var ceilinged = new Ceilinged(new MachineBudget(1, BudgetFileStatus.Rejected, BudgetError.InvalidCost));

        var job = await ceilinged.RunningAsync(Work);

        Assert.Equal([(job, HoldReason.InvalidBudget)], ceilinged.Jobs.Holds);
        Assert.Equal(
            new BudgetBreach(BudgetMeasure.Declaration, "budgets.json", 0, 0, BudgetError.InvalidCost),
            Assert.Single(ceilinged.Book.OfJob(job)).Breach);
        Assert.Empty(ceilinged.History.Asked);
    }

    [Fact]
    public async Task WithoutACeilingNothingIsHeldAndTheUsageHistoryIsNotReadAsync()
    {
        var ceilinged = new Ceilinged(Machine() with { RunningJobs = 2 });
        _ = await ceilinged.RunningAsync(Work);

        await ceilinged.SpentAsync(Today, 1_000m, (Work, 1_000m));

        Assert.Empty(ceilinged.Jobs.Holds);
        Assert.Empty(ceilinged.History.Asked);
    }

    private static MachineBudget Machine() => new(Option<int>.None, BudgetFileStatus.Applied, Option<BudgetError>.None);

    private sealed class Ceilinged
    {
        private readonly SpendCeiling ceiling;

        public Ceilinged(MachineBudget machine)
        {
            Clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("east", TimeSpan.FromHours(2), "east", "east"));
            Book = new BudgetBook(new Budgeted.FixedMachine { Budget = machine }, new InMemoryInterventions());
            ceiling = new SpendCeiling(new Budgeted.FixedMachine { Budget = machine }, History, new BudgetActions(Book, Jobs, Bus, Clock), Clock);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 11, 23, 30, 0, TimeSpan.Zero));

        public Budgeted.HoldingJobs Jobs { get; } = new(Option<JobRejection>.None);

        public RecordingBus Bus { get; } = new();

        public BudgetBook Book { get; }

        public Budgeted.DailyHistory History { get; } = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public async Task<JobId> RunningAsync(ConnectionName connection)
        {
            var (job, session) = (JobId.New(), SessionId.New());
            await ceiling.HandleAsync(new SessionOpened(session, Budgeted.Provider, "/worktrees/1", connection), Cancellation);
            await ceiling.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);
            await ceiling.HandleAsync(new JobSessionStarted(job, session), Cancellation);

            return job;
        }

        public async Task ProgressAsync(JobId job, JobStatus status) => await ceiling.HandleAsync(new JobProgressed(job, status), Cancellation);

        public async Task SpentAsync(DateOnly day, decimal total, params (ConnectionName Connection, decimal Amount)[] byConnection)
        {
            History.Spent[day] = (total, byConnection);
            await ceiling.HandleAsync(new UsageRecorded(SessionId.New(), Option<JobId>.None), Cancellation);
        }
    }
}
