using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DocumentationTests(PublishedPlugins plugins)
{
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
        Assert.Contains("## Evidence {#evidence}", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "v2-as-built.md"), Cancellation));
        Assert.Contains("| v2 | As built | [v2-as-built.md](v2-as-built.md) |", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "README.md"), Cancellation));
        Assert.Equal(JobStatus.Discarded, writer.Status);
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

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
    }
}
