using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ClaudeCodeDocumentationTests(PublishedPlugins plugins)
{
    private const string Instruction = "Track the work with tasks: write GREETING.md, then polish it.";

    private const string DocumentedOnTheSimulator = """{ "connection": "sim", "docs": { "enabled": true } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ClaudeCodesTaskToolsUpdateTheJobsPlanAndTheCompanionWritesTheFeaturesVersionsFromItAsync()
    {
        var documented = await DocumentedAsync("tasks");

        Assert.Equal([("Write GREETING.md", PlanStepStatus.Done), ("Polish the greeting", PlanStepStatus.Done)], documented.Plan);
        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.AsBuilt)], documented.Versions);
        Assert.Equal("# Plan v1\n\n1. Write GREETING.md\n2. Polish the greeting\n", documented.FirstPlan);
    }

    [Fact]
    public async Task ACompactionInTheMiddleOfATurnKeepsTheJobsPlanAndTheCompanionCarriesOnFromItsFirstVersionAsync()
    {
        var documented = await DocumentedAsync("tasks-compacted");

        Assert.Equal([("Write GREETING.md", PlanStepStatus.Done), ("Polish the greeting", PlanStepStatus.Done)], documented.Plan);
        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.AsBuilt)], documented.Versions);
        Assert.Equal("# Plan v1\n\n1. Write GREETING.md\n2. Polish the greeting\n", documented.FirstPlan);
    }

    private async Task<Documented> DocumentedAsync(string transcript)
    {
        using var claude = await TranscribedClaude.PrepareAsync(transcript);
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            claude.Data,
            [(".avala/permissions.json", """{ "autonomy": "autonomous" }"""), (".avala/jobs.json", DocumentedOnTheSimulator)],
            claudeCode: Option<IPlugin>.Some(claude.Plugin));
        var started = run.Watch<JobSessionStarted>();
        var activity = run.Watch<AgentActivity>();
        var written = run.Watch<VersionWritten>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction) { Connection = new ConnectionName(TranscribedClaude.Connection) }));

        var session = (await started.UntilAsync(began => began.Job == job)).Session;
        var plans = (await activity.CollectUntilAsync(update => update.Event is TurnCompleted completed && completed.Session == session))
            .Select(update => update.Event).OfType<PlanUpdated>().Where(updated => updated.Session == session).ToList();
        var versions = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
        var slug = Outcomes.Present(run.Get<IDocumentation>().OfJob(job)).Feature.Slug;
        var first = Path.Combine(worktree, "docs", "features", slug, "plans", "v1.md");

        return new Documented(
            [.. (plans.Count == 0 ? [] : plans[^1].Steps).Select(step => (step.Title, step.Status))],
            [.. versions.Where(version => version.Job == job).Select(version => (version.Version.Number, version.Version.Kind))],
            File.Exists(first) ? await File.ReadAllTextAsync(first, Cancellation) : string.Empty);
    }

    private sealed record Documented(IReadOnlyList<(string Title, PlanStepStatus Status)> Plan, IReadOnlyList<(int Number, VersionKind Kind)> Versions, string FirstPlan);
}
