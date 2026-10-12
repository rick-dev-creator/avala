using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ScratchFolderTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsTemporaryFilesGoToItsOwnScratchFolderWhichThePolicyTreatsAsInsideItsWorkspaceAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "scratch-files", (".avala/permissions.json", Autonomous));

        var decisions = await run.DecisionsAsync(2);
        var scratch = ScratchFolders.Of(new AvalaPaths(run.DataFolder), run.Worktree);

        Assert.Equal(
            [(Path.Combine(scratch, "notes.txt"), PolicyAnswer.Allow, "edits-inside-the-workspace"), (Path.GetFullPath(Path.Combine(run.Worktree, "..", "escape.txt")), PolicyAnswer.Deny, "edits-outside-the-workspace-go-to-a-human")],
            decisions.Select(decision => (decision.Target, decision.Answer, decision.Rule.Match(rule => rule.Name, () => string.Empty))));
        Assert.Equal("Scratch notes\n", await File.ReadAllTextAsync(Path.Combine(scratch, "notes.txt"), Cancellation));
        Assert.DoesNotContain(run.Worktree, scratch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJobsScratchFolderIsRemovedWhenTheJobEndsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "scratch-files", (".avala/permissions.json", Autonomous));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var scratch = ScratchFolders.Of(new AvalaPaths(run.DataFolder), run.Worktree);
        Assert.True(Directory.Exists(scratch));

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        await run.DeliveredAsync();

        Assert.False(Directory.Exists(scratch));
    }
}
