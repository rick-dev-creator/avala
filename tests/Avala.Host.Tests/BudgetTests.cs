using Avala.Documentation.Contracts;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;

namespace Avala.Host.Tests;

public sealed class BudgetTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

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
    public async Task TheNoteThatABudgetRaiseContinuesADocumentedJobWithIsNoChangeRequestOfThePersonAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns", (".avala/budget.json", """{ "costPerJob": { "USD": 0.012 } }"""), (".avala/jobs.json", """{ "docs": { "enabled": true } }"""));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        _ = await run.BudgetInterventionAsync();
        var workbench = await run.WorkbenchAsync();
        var usage = Assert.Single(await workbench.InspectAsync(run.Job, "UsageSectionViewModel"));
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["CanRaise"].Value<bool>(), () => "Raise was not offered");

        await run.Ui.RunAsync(() => usage.ExecuteAsync("RaiseCommand"));

        _ = await run.SettledAsync();
        await run.DeliveredAsync();
        Assert.Single(run.Get<IBudgets>().RaisesOf(run.Job));
        Assert.Empty(run.Get<IDocumentation>().OfJob(run.Job).Match(documented => documented.Requests, () => []));
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
    public async Task ATurnThatTakesTheMachinesSpendOfTheDayPastItsCeilingOnItsFinishingLineIsHeldBeforeItsChecksRunAsync()
    {
        var recording = new DogfoodScript("daily").Turn().Say("Done.").Bill(0.0120m, 0.22).Finish();
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            $"{recording.Tag} Greet the team",
            [("budgets.json", """{ "spendPerDay": { "USD": 0.001 } }"""), ("recordings/daily.json", recording.Json)],
            [(".avala/checks.json", PassingChecks)]);

        var journey = await run.JourneyAsync();

        Assert.Equal(JobStatus.NeedsHelp, journey[^1]);
        Assert.DoesNotContain(JobStatus.AwaitingReview, journey);
        Assert.Empty(run.Get<IVerifications>().OfJob(run.Job));
        var hold = await run.BudgetInterventionAsync();
        Assert.Equal((run.Job, HoldReason.BudgetExceeded), (hold.Hold.Job, hold.Hold.Reason));
        Assert.Equal(new BudgetBreach(BudgetMeasure.DailySpend, "USD", 0.0120m, 0.001m, Option<BudgetError>.None), hold.Breach);
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
