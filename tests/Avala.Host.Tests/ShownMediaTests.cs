using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class ShownMediaTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AShownImageAppearsInTheConversationFromACopyKeptOutsideTheWorktreeAndIsRecalledAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "show-media");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;

        var before = await ShownAsync(run);
        await run.DeliveredAsync();
        await run.RestartAsync();
        await run.StartedAsync();
        var after = await ShownAsync(run);

        Assert.Equal(("checkout.png", "The checkout page after the fix"), (before.Name, before.Caption));
        Assert.False(before.Path.StartsWith(worktree, StringComparison.Ordinal));
        Assert.Equal("PNG\n", await File.ReadAllTextAsync(before.Path, TestContext.Current.CancellationToken));
        Assert.Equal(before, after);
    }

    private static async Task<(string Name, string Caption, string Path)> ShownAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Contains("MediaViewModel"));

        return await run.Ui.ReadAsync(() =>
        {
            var media = OpenWorkbench.Entry(conversation, "MediaViewModel")!.Value;

            return (media["Name"].Text, media["Caption"].Text, media["Media"]["Path"].Text);
        });
    }
}
