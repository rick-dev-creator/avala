using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class UsageSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobInFocusShowsWhatItSpentAgainstItsCapsAndCarve()
    {
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(
            ("0.25 USD · 1,500 tokens", "Cost cap 5 USD", "Carved 50% of its parent's budget · 0.5 USD"),
            (section.Spent, string.Join("|", section.Caps), section.Carve));
    }

    [Fact]
    public async Task AJobHeldByItsBudgetListsTheIntervention()
    {
        var job = bench.Job("Extract sync queue into a module", JobStatus.NeedsHelp);
        bench.Audit.Interventions.Add(new BudgetIntervention(
            new JobHold(job.Job, SessionId.New(), HoldReason.BudgetExceeded, SessionHalt.Interrupted),
            new BudgetBreach(BudgetMeasure.Cost, "USD", 5.2m, 5m, Option<BudgetError>.None),
            DateTimeOffset.UnixEpoch));
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(["Held for Cost: 5.2 of 5 USD"], section.Interventions);
        Assert.Equal("No usage reported", section.Spent);
    }

    [Fact]
    public async Task UsageRecordedLaterShowsOnceTheJobsRevisionMoves()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);
        await section.FocusAsync(job.Job, bench);
        var before = section.Spent;
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Publish(Bench.OnBoard(job, revision: 1)), Cancellation);

        Assert.Equal(("No usage reported", "0.25 USD · 1,500 tokens"), (before, section.Spent));
    }

    [Fact]
    public async Task TheHeaderFactAndTheMeterSetTheSpendAgainstTheCostCap()
    {
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("0.25 USD of 5 USD", false), (section.Fact, section.IsAttention));
        var meter = Assert.IsAssignableFrom<Components.Meters.IMeterViewModel>(section.Meter);
        Assert.Equal(("Cost cap", 0.05, "5%"), (meter.Label, Math.Round(meter.Fraction, 2), meter.Reading));
    }

    [Fact]
    public async Task AJobWithoutCapOrUsageHasNoMeterAndAHoldReadsInAmber()
    {
        var job = bench.Job("Extract sync queue into a module", JobStatus.NeedsHelp);
        bench.Audit.Interventions.Add(new BudgetIntervention(
            new JobHold(job.Job, SessionId.New(), HoldReason.BudgetExceeded, SessionHalt.Interrupted),
            new BudgetBreach(BudgetMeasure.Cost, "USD", 5.2m, 5m, Option<BudgetError>.None),
            DateTimeOffset.UnixEpoch));
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(("nothing yet", true, null), (section.Fact, section.IsAttention, section.Meter));
    }

    [Theory]
    [InlineData(HoldReason.BudgetExceeded, BudgetMeasure.Cost, JobStatus.NeedsHelp, true)]
    [InlineData(HoldReason.BudgetExceeded, BudgetMeasure.Tokens, JobStatus.NeedsHelp, true)]
    [InlineData(HoldReason.BudgetExceeded, BudgetMeasure.Cost, JobStatus.Running, false)]
    [InlineData(HoldReason.BudgetExceeded, BudgetMeasure.DailySpend, JobStatus.NeedsHelp, false)]
    [InlineData(HoldReason.LimitNearlyReached, BudgetMeasure.Limit, JobStatus.NeedsHelp, false)]
    public async Task OnlyAJobStillHeldOverItsCostOrTokenCapOffersToRaiseItsBudgetAsync(HoldReason reason, BudgetMeasure measure, JobStatus status, bool offered)
    {
        var job = Held(reason, measure, status);
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(job, bench);

        Assert.Equal((offered, offered), (section.CanRaise, section.RaiseCommand.CanExecute(null)));
    }

    [Fact]
    public async Task RaisingTheBudgetRaisesTheJobAndContinuesItTellingTheAgentByHowMuchAsync()
    {
        var job = Held(HoldReason.BudgetExceeded, BudgetMeasure.Cost);
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);
        await section.FocusAsync(job, bench);

        await section.RaiseCommand.ExecuteAsync(null);

        Assert.Equal([job], bench.Audit.Raises.Select(raise => raise.Job));
        Assert.Equal(["continue Your budget was raised by 1.2 USD. Carry on where you left off."], bench.Jobs.Calls);
        Assert.Equal(string.Empty, section.Notice);
    }

    [Fact]
    public async Task ARefusedRaiseSaysWhyAndLeavesTheJobHeldAsync()
    {
        var job = Held(HoldReason.BudgetExceeded, BudgetMeasure.Cost);
        bench.Audit.Refusal = BudgetError.NotOverBudget;
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);
        await section.FocusAsync(job, bench);

        await section.RaiseCommand.ExecuteAsync(null);

        Assert.Empty(bench.Jobs.Calls);
        Assert.Equal("The budget was not raised: the job is no longer held over its budget", section.Notice);
    }

    [Fact]
    public async Task TheRaisesOfAJobAreListedWithWhatTheyAddedAsync()
    {
        var job = Held(HoldReason.BudgetExceeded, BudgetMeasure.Cost);
        bench.Audit.Raises.AddRange(
        [
            new BudgetRaise(job, new SpendCaps([new Cost(1.2m, "USD")], 1_000L), DateTimeOffset.UnixEpoch),
            new BudgetRaise(job, new SpendCaps([new Cost(0.4m, "USD")], Option<long>.None), DateTimeOffset.UnixEpoch) { Child = JobId.New() },
        ]);
        using var section = new UsageSectionViewModel(bench.Inspected(), bench.Raising);

        await section.FocusAsync(job, bench);

        Assert.Equal(["Budget raised by 1.2 USD · 1,000 tokens", "Budget raised by 0.4 USD for a sub-agent"], section.Raises);
    }

    public void Dispose() => bench.Dispose();

    private JobId Held(HoldReason reason, BudgetMeasure measure, JobStatus status = JobStatus.NeedsHelp)
    {
        var job = bench.Job("Extract sync queue into a module", status);
        bench.Audit.Interventions.Add(new BudgetIntervention(
            new JobHold(job.Job, SessionId.New(), reason, SessionHalt.Interrupted),
            new BudgetBreach(measure, "USD", 5.2m, 5m, Option<BudgetError>.None),
            DateTimeOffset.UnixEpoch));
        bench.Jobs.Status = JobStatus.NeedsHelp;

        return job.Job;
    }
}
