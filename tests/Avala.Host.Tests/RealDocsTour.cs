using System.Text.Json.Nodes;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class RealDocsTour(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Brief = " Be brief.";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? Path.Combine(Path.GetTempPath(), "avala-docs-tour"));

    private static string Folder => FeatureCamera.Folder ?? Path.Combine(Path.GetTempPath(), "avala-docs-tour");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    [Fact]
    public async Task ThreeRealJobsDocumentTheirFeaturesWhichAreSeenInTheProjectThePaneAndTheFeaturePageAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        Assert.SkipUnless(HeadlessApp.Renders, "Set AVALA_HEADLESS_RENDER=1 so the tour can take screenshots.");
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
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", connections.ToJsonString())],
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/checks.json", """{ "checks": [{ "name": "git", "command": "git", "arguments": ["--version"] }] }"""),
                (".avala/jobs.json", """{ "approval": "keep", "docs": { "enabled": true }, "delegation": { "roles": { "research": { "model": "haiku", "effort": "low" } } } }"""),
                ("README.md", "# Toolbox\n\nSmall Python tools.\n"),
            ],
            harnesses: true);
        await run.Repository.GitAsync(Cancellation, "config", "user.name", "Ada");
        await run.Repository.GitAsync(Cancellation, "config", "user.email", "ada@localhost");
        var written = run.Watch<VersionWritten>();
        var workbench = await run.WorkbenchAsync();

        var greeter = await DocumentedAsync(run, written, "Greeter command line\n\nCreate greet.py that prints 'Hello, NAME!' for a name given as its first argument, and 'Hello, world!' without one." + Brief);
        var conversation = await workbench.SelectAsync(greeter);
        var pane = await OpenPaneAsync(run);
        await Camera.ShootAsync(run, "docs-tour-01-job-and-its-docs-pane");

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(greeter, "Add a --shout flag that prints the greeting in uppercase.", Cancellation));
        _ = await written.UntilAsync(version => version.Job == greeter && version.Version.Kind == VersionKind.AsBuilt);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Contains("ChangeLineViewModel"));
        await Camera.ShootAsync(run, "docs-tour-02-change-request-and-new-version");
        await Camera.ShootAsync(run, "docs-tour-03-docs-pane-history", window => FeatureCamera.ScrollTo(window, "History"));
        var steps = await run.Ui.ReadAsync(() => pane["History"]["Steps"].Items);
        await run.Ui.InvokeAsync(() => steps[0].Execute("ChooseCommand"), Cancellation);
        await Camera.ShootAsync(run, "docs-tour-04-first-plan-chosen-in-history", window => FeatureCamera.ScrollTo(window, "What happened"));
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(greeter, Cancellation));

        var calculator = await DocumentedAsync(run, written, "Calculator module\n\nCreate calc.py with add(a, b) and sub(a, b), and test_calc.py that checks both with unittest." + Brief);
        _ = Outcomes.Succeeds(await run.Get<IOpenDeliveries>().ApproveThroughAsync(calculator, "merge", Cancellation));

        _ = await DocumentedAsync(run, written, "Changelog script\n\nCreate changelog.py that prints the last five git commit subjects as a Markdown list." + Brief);

        var group = await run.Ui.ReadAsync(() => workbench.Sidebar["Projects"].Items.Single());
        var docs = await run.Ui.ReadAsync(() => group["Docs"]);
        await workbench.ShowsAsync(() => docs["Features"].Items.Count == 3);
        if (!await run.Ui.ReadAsync(() => docs["IsOpen"].Value<bool>()))
        {
            await run.Ui.InvokeAsync(() => docs.Execute("ToggleCommand"), Cancellation);
        }

        await Camera.ShootAsync(run, "docs-tour-05-project-docs-merged-and-not-merged");

        var page = run.Page("Docs");
        foreach (var (row, name) in new[] { (0, "docs-tour-06-feature-page"), (1, "docs-tour-08-unmerged-feature-page") })
        {
            var feature = await run.Ui.ReadAsync(() => docs["Features"].Items[row]);
            await run.Ui.InvokeAsync(() => feature.Execute("OpenCommand"), Cancellation);
            await run.Ui.RunAsync(() => page["Loading"].Value<Task>());
            await Camera.ShootAsync(run, name);
            await Camera.ShootAsync(run, $"{name}-light", ThemeVariant.Light);
        }

        await run.Ui.InvokeAsync(() => page["Player"].Execute("NextCommand"), Cancellation);
        await run.Ui.InvokeAsync(() => page["Player"].Execute("NextCommand"), Cancellation);
        await Camera.ShootAsync(run, "docs-tour-09-player-stepping-through-the-c4");

        await CopyAsync(Path.Combine(run.Repository.Path, "docs", "features"), Path.Combine(Folder, "markdown", "merged"));
        await CopyAsync(Path.Combine(await WorktreeAsync(run, greeter), "docs", "features"), Path.Combine(Folder, "markdown", "on-its-branch"));
    }

    private static async Task<JobId> DocumentedAsync(SimulatedRun run, EventWatch<VersionWritten> written, string instruction)
    {
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, instruction)));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var progressed = run.Watch<JobProgressed>();

        if (Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status != JobStatus.AwaitingReview)
        {
            _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        }

        return job;
    }

    private static async Task<Bound> OpenPaneAsync(SimulatedRun run)
    {
        var contributions = run.Get<IEnumerable<RegionContribution>>().ToList();
        var toggle = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.JobHeader && contribution.ViewModel.GetType().Name == "DocsToggleViewModel").ViewModel);
        var pane = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.Preview && contribution.ViewModel.GetType().Name == "DocsPaneViewModel").ViewModel);
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "the docs glyph is not shown");
        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["IsOpen"].Value<bool>() && pane["State"].Text.EndsWith("versions", StringComparison.Ordinal), () => $"the pane says {pane["State"].Text}");

        return pane;
    }

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return Outcomes.Succeeds(await run.Get<Workspaces.Contracts.IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
    }

    private static async Task CopyAsync(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, await File.ReadAllTextAsync(file, Cancellation), Cancellation);
        }
    }
}
