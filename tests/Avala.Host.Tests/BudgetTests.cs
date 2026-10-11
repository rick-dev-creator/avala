using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
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
}
