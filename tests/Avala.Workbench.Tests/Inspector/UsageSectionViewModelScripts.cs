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
        using var section = new UsageSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(
            ("USD 0.25 · 1,500 tokens", "Cost cap USD 5", "Carved 50% of its parent's budget · USD 0.5"),
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
        using var section = new UsageSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(["Held for Cost: 5.2 of 5 USD"], section.Interventions);
        Assert.Equal("No usage reported", section.Spent);
    }

    [Fact]
    public async Task UsageRecordedLaterShowsOnceTheJobsRevisionMoves()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        using var section = new UsageSectionViewModel(bench.Inspected());
        await section.FocusAsync(job.Job, bench);
        var before = section.Spent;
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Publish(Bench.OnBoard(job, revision: 1)), Cancellation);

        Assert.Equal(("No usage reported", "USD 0.25 · 1,500 tokens"), (before, section.Spent));
    }

    [Fact]
    public async Task TheHeaderFactAndTheMeterSetTheSpendAgainstTheCostCap()
    {
        using var section = new UsageSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("USD 0.25 of USD 5", false), (section.Fact, section.IsAttention));
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
        using var section = new UsageSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(("nothing yet", true, null), (section.Fact, section.IsAttention, section.Meter));
    }

    public void Dispose() => bench.Dispose();
}
