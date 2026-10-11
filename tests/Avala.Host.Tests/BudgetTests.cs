using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class BudgetTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AJobHeldAtItsLastTurnForItsCostCapIsRaisedFromTheInspectorAndContinuesToReviewAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns", (".avala/budget.json", """{ "costPerJob": { "USD": 0.012 } }"""));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        _ = await run.BudgetInterventionAsync();
        var workbench = await run.WorkbenchAsync();
        var usage = Assert.Single(await workbench.InspectAsync(run.Job, "UsageSectionViewModel"));
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["CanRaise"].Value<bool>(), () => "Raise was not offered");

        await run.Ui.RunAsync(() => usage.ExecuteAsync("RaiseCommand"));

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var raise = Assert.Single(run.Get<IBudgets>().RaisesOf(run.Job));
        Assert.Equal(0.014m, raise.By.Cost.Single().Amount);
        Assert.Equal(string.Empty, await run.Ui.ReadAsync(() => usage["Notice"].Text));
    }

    [Fact]
    public async Task ARaisedJobThatOvershootsItsRaisedCapInTheSameSessionIsHeldAndAuditedAgainAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns", (".avala/budget.json", """{ "costPerJob": { "USD": 0.005 } }"""));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var first = await run.BudgetInterventionAsync();
        var workbench = await run.WorkbenchAsync();
        var usage = Assert.Single(await workbench.InspectAsync(run.Job, "UsageSectionViewModel"));
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["CanRaise"].Value<bool>(), () => "Raise was not offered");

        await run.Ui.RunAsync(() => usage.ExecuteAsync("RaiseCommand"));

        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var second = await run.BudgetInterventionAsync();
        Assert.Equal((first.Hold.Session, first.Hold.Reason), (second.Hold.Session, second.Hold.Reason));
        Assert.Equal(0.023m, second.Breach.Measured);
        Assert.Equal([first, second], run.Get<IBudgets>().OfJob(run.Job));
    }

    [Fact]
    public async Task AJobThatTakesTheMachinesSpendOfTheDayToItsCeilingIsHeldWithTheDailySpendAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("permission"), [("budgets.json", """{ "spendPerDay": { "USD": 0.01 } }""")], []);

        var hold = await run.BudgetInterventionAsync();

        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        Assert.Equal((run.Job, HoldReason.BudgetExceeded), (hold.Hold.Job, hold.Hold.Reason));
        Assert.Equal(new BudgetBreach(BudgetMeasure.DailySpend, "USD", 0.0110m, 0.01m, Option<BudgetError>.None), hold.Breach);
    }

    [Fact]
    public async Task WhatJobsSpentTodayBeforeARestartCountsTowardsTheCeilingAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("budgets.json", """{ "spendPerDay": { "USD": 0.015 } }""")], []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.RestartAsync();

        var next = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("permission"))));

        var hold = await run.BudgetInterventionAsync();
        Assert.Equal(next, hold.Hold.Job);
        Assert.Equal(new BudgetBreach(BudgetMeasure.DailySpend, "USD", 0.0152m, 0.015m, Option<BudgetError>.None), hold.Breach);
    }
}
