using System.Windows.Input;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ScratchRepositoryTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobStartedWithoutARepositoryRunsInAScratchRepositoryReachesReviewMarkedScratchAndApprovingMergesItThereAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var workbench = await run.WorkbenchAsync();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });

        await run.Ui.RunAsync(() =>
        {
            page.Set("Instruction", SimulatedRun.Simulate("plan-across-turns"));

            return page.ExecuteAsync("StartWithoutRepositoryCommand");
        });

        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var scratch = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Repository;
        Assert.Equal(Path.Combine(Path.GetFullPath(run.DataFolder), "scratch"), Path.GetDirectoryName(scratch));
        Assert.StartsWith("simulate-plan-across-turns-", Path.GetFileName(scratch)[11..], StringComparison.Ordinal);
        await workbench.ShowsInGroupAsync(job, "ReadyForReview");
        Assert.True(await run.Ui.ReadAsync(() => workbench.Sidebar["ReadyForReview"].Items.Single(row => row["Job"].Value<JobId>() == job)["IsScratch"].Value<bool>()));
        var worktree = Assert.Single(await workbench.InspectAsync(job, "WorktreeSectionViewModel"));
        Assert.Equal((true, scratch), await run.Ui.ReadAsync(() => (worktree["IsScratch"].Value<bool>(), worktree["Repository"].Text)));

        var approval = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));

        Assert.Equal("merge", approval.Delivery.Strategy);
        Assert.Equal("# Hello\n", (await File.ReadAllTextAsync(Path.Combine(scratch, "GREETING.md"), Cancellation)).ReplaceLineEndings("\n"));
        Assert.Equal((Outcomes.Present(approval.Delivery.Commit), string.Empty), (await GitAsync(run, scratch, "rev-parse", "HEAD"), await GitAsync(run, scratch, "status", "--porcelain")));
    }

    private static async Task<string> GitAsync(SimulatedRun run, string folder, params string[] arguments) =>
        (await run.Get<IProcessRunner>().RunAsync(new ProcessRequest("git", ["-C", folder, .. arguments]), Cancellation)).Match(outcome => outcome.Output.Trim(), error => error.ToString());
}
