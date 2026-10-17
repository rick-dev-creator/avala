using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DocumentationTests(PublishedPlugins plugins)
{
    private const string Feedback = "Stripe sends a refund twice; apply it once.";

    private const string DocsOn = """{ "docs": { "enabled": true } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsPlanIsWrittenByADocumentationChildAsItsFeaturesFirstVersionAndItsReviewAsBuiltInTheJobsWorktreeAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-across-turns] Add the changelog")));
        var versions = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        var documented = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        var folder = $"docs/features/{documented.Feature.Slug}";
        var worktree = await WorktreeAsync(run, job);
        var writer = Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
        Assert.Equal(
            [(1, VersionKind.Proposed, $"{folder}/v1-proposed.md"), (2, VersionKind.AsBuilt, $"{folder}/v2-as-built.md")],
            versions.Where(version => version.Job == job).Select(version => (version.Version.Number, version.Version.Kind, version.Version.Path)));
        Assert.Equal(Option<JobId>.Some(writer.Job), versions[0].Version.Writer);
        Assert.Contains("```mermaid\nC4Container", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "v1-proposed.md"), Cancellation));
        Assert.Equal("# Plan v1\n\n1. Read the code\n2. Write the change\n3. Run the tests\n", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "plans", "v1.md"), Cancellation));
        Assert.Contains("## Evidence\n", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "v2-as-built.md"), Cancellation));
        Assert.Contains("| v2 | As built | [v2-as-built.md](v2-as-built.md) |", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "README.md"), Cancellation));
        Assert.Equal(JobStatus.Discarded, writer.Status);
    }

    [Fact]
    public async Task TheJobsConversationShowsAQuietLineForEachVersionWrittenAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-across-turns] Add the changelog")));
        _ = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "DocsLineViewModel") == 2);

        Assert.Equal(2, await run.Ui.ReadAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "DocsLineViewModel")));
    }

    [Fact]
    public async Task WithoutTheDocsSectionAJobsPlanStartsNoDocumentationChildAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();

        Assert.Empty(await run.Get<IJobCatalog>().ChildrenAsync(run.Job, Cancellation));
        Assert.Equal(Option<JobDocumentation>.None, run.Get<IDocumentation>().OfJob(run.Job));
    }

    [Fact]
    public async Task ASendBackThatChangesThePlanIsRecordedAsAChangeRequestThatTheDeviationWrittenNextLinksInChangesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));

        var (job, versions) = await SentBackAsync(run);

        var documented = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        var folder = Path.Combine(await WorktreeAsync(run, job), "docs", "features", documented.Feature.Slug);
        Assert.Equal(
            [(1, VersionKind.Proposed), (2, VersionKind.AsBuilt), (3, VersionKind.Deviation), (4, VersionKind.AsBuilt)],
            versions.Select(version => (version.Number, version.Kind)));
        var request = Assert.Single(documented.Requests);
        Assert.Equal((RequestKind.SendBack, Feedback, 2, Option<int>.Some(3)), (request.Kind, request.Text, request.Attempt, request.Version));
        var changes = await File.ReadAllTextAsync(Path.Combine(folder, "changes.md"), Cancellation);
        Assert.Contains($"> {Feedback}\n", changes);
        Assert.Contains("v3-deviation.md", changes);
        Assert.Contains("1. Add a Webhooks container\n2. Keep idempotency keys in the ledger\n", await File.ReadAllTextAsync(Path.Combine(folder, "plans", "v3.md"), Cancellation));
    }

    [Fact]
    public async Task AJobsVersionsAndChangeRequestsSurviveARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var (job, _) = await SentBackAsync(run);
        var before = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));

        await run.RestartAsync();
        await run.StartedAsync();

        var after = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        Assert.Equal(before.Feature, after.Feature);
        Assert.Equal(before.Versions, after.Versions);
        Assert.Equal(before.Requests, after.Requests);
        Assert.Equal(Option<JobId>.None, after.Writing);
        var docs = Outcomes.Present(await run.Get<IDocumentation>().JobDocsAsync(job, Cancellation));
        Assert.Equal(4, Outcomes.Present(docs.Story).Versions.Count);
    }

    [Fact]
    public async Task CreatingTheSiteWritesItIntoTheRepositoryWithAvalasPlayerAndAJobFillsItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var documentation = run.Get<IDocumentation>();

        var created = Outcomes.Succeeds(await documentation.CreateSiteAsync(run.Repository.Path, Cancellation));

        var filler = Outcomes.Present(created.Filler);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(filler));
        Assert.Contains("\"template\": [ \"default\", \"modern\", \"template\" ]", await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, "docs", "docfx.json"), Cancellation));
        Assert.Contains("customElements.define(\"avala-c4\"", await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, "docs", "template", "public", "avala-c4.js"), Cancellation));
        Assert.True((await documentation.SiteAsync(run.Repository.Path, Cancellation)).Exists);
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(filler, Cancellation));
        Assert.Contains("docs/get-started/quickstart.md", history.Summary.Instruction);
    }

    private static async Task<(JobId Job, IReadOnlyList<FeatureVersion> Versions)> SentBackAsync(SimulatedRun run)
    {
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        var first = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));
        var then = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        return (job, [.. first.Concat(then).Where(version => version.Job == job).Select(version => version.Version)]);
    }

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
    }
}
