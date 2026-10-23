using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class DraftTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AJobWhoseDraftWasKeptAcrossARestartOpensWithItAndTheWorkbenchGoesQuietAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await run.WorkbenchAsync();
        var typed = await before.SelectAsync(run.Job);
        await run.Ui.InvokeAsync(() => typed["Composer"].Set("Draft", "q"), TestContext.Current.CancellationToken);

        await run.RestartAsync();
        await run.StartedAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => conversation["Composer"]["Draft"].Text == "q");

        await workbench.QuietAsync();
    }
}
