using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class DeliveredJobTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APullRequestOpenedFromTheReviewKeepsTheJobAmongTheOpenJobsAsDeliveredWithItsChecksAsync()
    {
        await using var run = await DeliveredAsync();
        var workbench = await run.WorkbenchAsync();
        var number = Outcomes.Present(run.Get<IPullRequests>().Of(run.Job)).PullRequest.Number;

        await workbench.ShowsInGroupAsync(run.Job, "Delivered", $"delivered · #{number} · checks 1 of 1");

        Assert.NotEqual("Done", (await workbench.RowAsync(run.Job, "Delivered")).Group);
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("closed")]
    public async Task ADeliveredJobFoldsUnderDoneOnceItsPullRequestIsMergedOrClosedAndSaysWhichAsync(string fate)
    {
        await using var run = await DeliveredAsync();
        var workbench = await run.WorkbenchAsync();
        var number = Outcomes.Present(run.Get<IPullRequests>().Of(run.Job)).PullRequest.Number;
        await workbench.ShowsInGroupAsync(run.Job, "Delivered", $"delivered · #{number} · checks 1 of 1");

        await new SimulatedForgeRemote(run.Repository.Remote).SetScenarioAsync(number, fate, Cancellation);
        _ = await run.Get<IPullRequests>().RefreshAsync(run.Job, Cancellation);

        await workbench.ShowsInGroupAsync(run.Job, "Done", $"{fate} · #{number}");
    }

    private async Task<SimulatedRun> DeliveredAsync()
    {
        var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), LocalForge, []);
        _ = await run.Repository.PublishAsync(Cancellation);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Outcomes.Succeeds(await run.Get<IOpenDeliveries>().ApproveThroughAsync(run.Job, PullRequestDelivery.Strategy, Cancellation));
        _ = await run.Get<IPullRequests>().RefreshAsync(run.Job, Cancellation);

        return run;
    }
}
