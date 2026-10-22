using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class RealDocumentationTests(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Instruction =
        "Create a tiny command-line Python script greet.py that prints 'Hello, NAME!' for a name given as its first argument, "
        + "and 'Hello, world!' without one, plus a README.md that says how to run it. "
        + "Before you change anything, record your plan as a task list of two or three steps with your task tools, and keep it updated as you go. Be brief.";

    private static readonly string[] Made = ["greet.py", "README.md"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Output => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_OUT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-real-claude");

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    [Fact]
    public async Task ARealJobIsDocumentedByARealCompanionFromItsPlanToItsAsBuiltAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        var connections = new JsonObject
        {
            ["default"] = "claude",
            ["connections"] = new JsonArray(new JsonObject
            {
                ["name"] = "claude",
                ["provider"] = "claude-code",
                ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
                ["settings"] = new JsonObject { ["model"] = "haiku" },
            }),
        };

        await using var run = await SimulatedRun.RealAsync(
            plugins,
            Instruction,
            [("connections.json", connections.ToJsonString())],
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "docs": { "enabled": true }, "delegation": { "roles": { "research": { "model": "haiku", "effort": "low" } } } }"""),
            ]);
        var written = run.Watch<VersionWritten>();

        var versions = await written.CollectUntilAsync(version => version.Job == run.Job && version.Version.Kind == VersionKind.AsBuilt);

        var documented = Outcomes.Present(run.Get<IDocumentation>().OfJob(run.Job));
        var worktree = await WorktreeAsync(run, run.Job);
        var folder = Path.Combine(worktree, "docs", "features", documented.Feature.Slug);
        await SaveAsync(worktree, folder, documented);
        Assert.Contains(versions, version => version.Job == run.Job && version.Version.Kind is VersionKind.Proposed or VersionKind.Deviation);
        Assert.True(File.Exists(Path.Combine(worktree, "greet.py")));
        Assert.Contains("```mermaid\nC4", await File.ReadAllTextAsync(Path.Combine(worktree, documented.Versions[0].Path), Cancellation));
    }

    private static async Task SaveAsync(string worktree, string folder, JobDocumentation documented)
    {
        var target = Directory.CreateDirectory(Path.Combine(Output, "real-docs")).FullName;

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        foreach (var file in Made.Where(name => File.Exists(Path.Combine(worktree, name))))
        {
            File.Copy(Path.Combine(worktree, file), Path.Combine(target, $"repo-{file}"), overwrite: true);
        }

        await File.WriteAllTextAsync(
            Path.Combine(target, "summary.json"),
            JsonSerializer.Serialize(new
            {
                feature = documented.Feature.Title,
                versions = documented.Versions.Select(version => $"v{version.Number} {version.Kind} {version.Path} attempt {version.Attempt}"),
                requests = documented.Requests.Select(request => $"#{request.Number} {request.Kind} {request.Text}"),
            }, Indented),
            Cancellation);
    }

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
    }
}
