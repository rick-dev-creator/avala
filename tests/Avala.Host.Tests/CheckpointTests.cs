using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class CheckpointTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobAwaitingReviewRestoresItsWorktreeToAnAttemptsCheckpointAndAnApprovedOneRefusesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation));
        var checkpoints = run.Get<IJobCheckpoints>();
        var listed = await checkpoints.OfAsync(job, Cancellation);
        var later = Path.Combine(worktree.Path, "later.md");
        await File.WriteAllTextAsync(later, "written after the attempt", Cancellation);

        var restored = Outcomes.Succeeds(await checkpoints.RestoreAsync(job, 1, Cancellation));

        Assert.Equal(1, Assert.Single(listed).Attempt);
        Assert.NotEmpty(listed[0].Files);
        Assert.False(File.Exists(later));
        Assert.Equal(["later.md"], restored.Files.Select(file => file.Path));
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        Assert.Equal(JobRejection.CannotRestore, Outcomes.FailsWith(await checkpoints.RestoreAsync(job, 1, Cancellation)));
    }
}
