using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ProjectDocsTests(PublishedPlugins plugins)
{
    private const string Keep = """{ "approval": "keep" }""";

    private const string DocsOn = """{ "approval": "keep", "docs": { "enabled": true } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DocumentFeaturesFromAProjectsMenuTurnsDocsOnInTheWorkingCopyAndItsCommitCommitsThatFileAloneAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", Keep));
        await IdentityAsync(run);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var docs = await ProjectDocsAsync(await run.WorkbenchAsync(), run);
        Assert.False(await run.Ui.ReadAsync(() => docs["IsOn"].Value<bool>()));

        await run.Ui.RunAsync(() => docs.ExecuteAsync("SwitchCommand"));

        Assert.Equal(
            (true, true, "On in your working copy only: new jobs run without docs until .avala/jobs.json is committed."),
            await run.Ui.ReadAsync(() => (docs["IsOn"].Value<bool>(), docs["IsUncommitted"].Value<bool>(), docs["Note"].Text)));
        await run.Ui.RunAsync(() => docs.ExecuteAsync("CommitCommand"));
        Assert.Equal([".avala/jobs.json"], (await run.Repository.GitAsync(Cancellation, "show", "--name-only", "--format=", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal((true, false), await run.Ui.ReadAsync(() => (docs["IsOn"].Value<bool>(), docs["IsUncommitted"].Value<bool>())));
    }

    [Fact]
    public async Task AFeatureDocumentedOnABranchThatIsNotMergedIsListedInItsProjectMarkedSoAndOpensItsVersionsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var first = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(first));
        var workbench = await run.WorkbenchAsync();
        var written = run.Watch<VersionWritten>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));

        Assert.False(Directory.Exists(Path.Combine(run.Repository.Path, "docs")));
        var docs = await ProjectDocsAsync(workbench, run);
        await workbench.ShowsAsync(() => docs["Features"].Items.Count == 1);
        var row = await run.Ui.ReadAsync(() => docs["Features"].Items[0]);
        Assert.Matches("^\\d+ versions · not merged$", await run.Ui.ReadAsync(() => row["Fact"].Text));
        Assert.Matches("^On branch avala/[0-9a-f]+, not merged yet$", await run.Ui.ReadAsync(() => row["Tip"].Text));

        await run.Ui.InvokeAsync(() => row.Execute("OpenCommand"), Cancellation);

        var page = run.Page("Docs");
        await run.Ui.RunAsync(() => page["Loading"].Value<Task>());
        Assert.True(await run.Ui.ReadAsync(() => page["Player"]["IsDrawn"].Value<bool>()));
        Assert.Matches("^repository · [a-z0-9-]+ · not merged, on avala/[0-9a-f]{8}$", await run.Ui.ReadAsync(() => page["Where"].Text));
    }

    [Fact]
    public async Task ApprovingADocumentedJobUnderKeepSaysItsDocsStayOnItsBranchUntilMergedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);

        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));

        await workbench.ShowsAsync(() => review["Status"].Value<JobStatus>() == JobStatus.Approved);
        Assert.Matches("^Branch avala/[0-9a-f]+ kept\\. Merge it when you are ready\\. Its docs stay on that branch until you merge it\\.$", await run.Ui.ReadAsync(() => review["Outcome"].Text));
    }

    private static async Task<Bound> ProjectDocsAsync(OpenWorkbench workbench, SimulatedRun run)
    {
        var group = await run.Ui.ReadAsync(() => workbench.Sidebar["Projects"].Items.Single());
        await run.Ui.RunAsync(() => group["DocsRead"].Value<Task>());

        return await run.Ui.ReadAsync(() => group["Docs"]);
    }

    private static async Task IdentityAsync(SimulatedRun run)
    {
        await run.Repository.GitAsync(Cancellation, "config", "user.name", "Ada");
        await run.Repository.GitAsync(Cancellation, "config", "user.email", "ada@localhost");
    }
}
