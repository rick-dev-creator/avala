using System.Windows.Input;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class PlanTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheJobsPlanStaysAcrossTurnsAndARestartUntilEveryStepIsDoneAndTheReviewShowsHowFarItGotAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var first = await PlanAsync(run, await run.WorkbenchAsync(), turns: 1);

        await run.RestartAsync();
        await run.StartedAsync();
        var workbench = await run.WorkbenchAsync();
        var restarted = await PlanAsync(run, workbench, turns: 1);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, "Go on with the plan.", Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var quiet = await PlanAsync(run, workbench, turns: 2);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, "Finish it.", Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var finished = await PlanAsync(run, workbench, turns: 3);

        IReadOnlyList<string> halfway =
        [
            "panel: True · 1 of 3 · Write the change",
            "review: Plan · 1 of 3 steps done · In progress · Write the change | Pending · Run the tests",
        ];
        Assert.Equal(halfway, first);
        Assert.Equal(halfway, restarted);
        Assert.Equal(halfway, quiet);
        Assert.Equal(["panel: False · 3 of 3 · ", "review: Plan · all 3 steps done · "], finished);
    }

    private static async Task<IReadOnlyList<string>> PlanAsync(SimulatedRun run, OpenWorkbench workbench, int turns)
    {
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Entries"].Items.Count(entry => entry.Kind == "TurnEndViewModel") == turns
            && ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        {
            var plan = conversation["Plan"];

            return
            [
                $"panel: {plan["IsShown"].Text} · {plan["Progress"].Text} · {plan["Current"].Text}",
                $"review: {review["Plan"].Text} · {string.Join(" | ", review["PlanLeft"].Value<IReadOnlyList<string>>())}",
            ];
        });
    }
}
