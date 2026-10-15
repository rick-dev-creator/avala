using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class MentionTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AFileMentionedInTheComposerReachesTheAgentWithWhatItRefersToAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, ("src/Rounding.cs", "class Rounding;"));
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("mentions"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.AwaitingReview);

        await run.Ui.InvokeAsync(() => conversation["Composer"].Set("Draft", "Look at @Rou"), TestContext.Current.CancellationToken);
        await await run.Ui.ReadAsync(() => (Task)conversation["Composer"]["Mentions"]["Looking"].Target);
        Assert.True(await run.Ui.ReadAsync(() => conversation["Composer"]["Mentions"]["IsOpen"].Value<bool>()));
        await run.Ui.InvokeAsync(() => ((System.Windows.Input.ICommand)conversation["Composer"]["Mentions"]["ChooseCommand"].Target).Execute(null), TestContext.Current.CancellationToken);
        var chosen = await run.Ui.ReadAsync(() => conversation["Composer"]["Draft"].Text);
        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", $"{chosen}first");

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });

        Assert.Equal("Look at @src/Rounding.cs ", chosen);
        Assert.Equal("Look at @src/Rounding.cs first\n\nReferenced:\n- @src/Rounding.cs: the file src/Rounding.cs in the job's worktree", await run.ToldAsync());
    }
}
