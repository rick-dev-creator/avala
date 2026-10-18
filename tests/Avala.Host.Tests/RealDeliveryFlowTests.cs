using System.Text.Json.Nodes;
using Avala.Documentation.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Shell;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Host.Tests;

public sealed class RealDeliveryFlowTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Research = "\"delegation\": { \"roles\": { \"research\": { \"model\": \"haiku\", \"effort\": \"low\" } } }";

    private const string GhStatus = """
        github.com
          ✓ Logged in to github.com account octocat (keyring)
          - Active account: true
        """;

    private static readonly Dictionary<string, ProcessOutcome> SignedIn = new(StringComparer.Ordinal) { ["gh"] = new ProcessOutcome(0, GhStatus, string.Empty) };

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? Path.Combine(Path.GetTempPath(), "avala-real-delivery"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    private static string Connections => new JsonObject
    {
        ["connections"] = new JsonArray(new JsonObject
        {
            ["name"] = "claude",
            ["provider"] = "claude-code",
            ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
            ["settings"] = new JsonObject { ["model"] = "haiku" },
        }),
    }.ToJsonString();

    [Fact]
    public async Task TheDocsSwitchStaysOnIsCommittedFromSettingsAndTheNextRealJobIsDocumentedAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [(".avala/permissions.json", Autonomous), (".avala/jobs.json", $$"""{ "approval": "keep", {{Research}} }""")], harnesses: true);
        await IdentityAsync(run);
        var docs = await DocsSettingAsync(run);
        await run.Ui.RunAsync(() => docs.ExecuteAsync("SwitchCommand"));

        var shownAgain = await DocsSettingAsync(run);
        Assert.Equal((true, true), await run.Ui.ReadAsync(() => (shownAgain["IsOn"].Value<bool>(), shownAgain["IsUncommitted"].Value<bool>())));
        await Camera.ShootAsync(run, "real-01-docs-switch-on-in-working-copy", ToDocs);
        await run.Ui.RunAsync(() => shownAgain.ExecuteAsync("CommitCommand"));
        Assert.Equal((true, false), await run.Ui.ReadAsync(() => (shownAgain["IsOn"].Value<bool>(), shownAgain["IsUncommitted"].Value<bool>())));
        await Camera.ShootAsync(run, "real-02-docs-switch-committed", ToDocs);

        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Plain("Create a tiny Python script greet.py that prints 'Hello, NAME!' for a name given as its first argument, and 'Hello, world!' without one."))));
        _ = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var toggle = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "DocsToggleViewModel");
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "no Docs glyph");
        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        await Camera.ShootAsync(run, "real-03-docs-written-by-the-next-job");

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        var group = await run.Ui.ReadAsync(() => workbench.Sidebar["Projects"].Items.Single());
        var projectDocs = await run.Ui.ReadAsync(() => group["Docs"]);
        await workbench.ShowsAsync(() => projectDocs["Features"].Items.Count == 1);
        Assert.Contains("not merged", await run.Ui.ReadAsync(() => projectDocs["Features"].Items[0]["Fact"].Text), StringComparison.Ordinal);
        await run.Ui.InvokeAsync(() => projectDocs.Execute("ToggleCommand"), Cancellation);
        await Camera.ShootAsync(run, "real-03b-unmerged-docs-listed-in-the-project");
    }

    [Fact]
    public async Task AFirstRealJobToldWhatTheRepositoryNeedsIsDeniedAWriteOutsideItsWorktreeWithItsReasonAndItsReviewNamesItsEndingAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [(".avala/permissions.json", Autonomous)], harnesses: true, signIns: SignedIn);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", "https://github.com/octocat/shop.git");
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Plain("First write a two-line pull request description to /tmp/avala-pr-body.md, then create greet.py that prints 'Hello, world!'. If a write is denied, carry on without it."))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        var card = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "ReadinessCardViewModel");
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());
        await run.Ui.PresentedAsync(card.Presentation, () => card["IsShown"].Value<bool>(), () => "no readiness card");
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is not null);
        Assert.Contains(
            "is outside the job's worktree, and writing there needs you.",
            await run.Ui.ReadAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value["Why"].Text),
            StringComparison.Ordinal);
        await Camera.ShootAsync(run, "real-04-readiness-card-and-denial-explained");

        await run.Ui.RunAsync(() => card["Items"].Items[0].ExecuteAsync("ActCommand"));
        Assert.True(await run.Ui.ReadAsync(() => card["Items"].Items[0]["IsDone"].Value<bool>()));
        await Camera.ShootAsync(run, "real-05-forge-connected-from-the-card");

        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        await workbench.ShowsAsync(() => review["PullRequestAction"].Text.Length > 0);
        Assert.Equal(("Open pull request on github", true), await run.Ui.ReadAsync(() => (review["PullRequestAction"].Text, review["PullRequestFirst"].Value<bool>())));
        Assert.StartsWith("Keep branch avala/", await run.Ui.ReadAsync(() => review["ApproveLabel"].Text), StringComparison.Ordinal);
        await Camera.ShootAsync(run, "real-06-review-names-its-actions");

        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));
        await workbench.ShowsAsync(() => review["Status"].Value<JobStatus>() == JobStatus.Approved);
        Assert.EndsWith("kept. Merge it when you are ready.", await run.Ui.ReadAsync(() => review["Outcome"].Text), StringComparison.Ordinal);
        await Camera.ShootAsync(run, "real-07-approved-says-what-happened");
    }

    [Fact]
    public async Task APullRequestOfABranchTheRealAgentMadeAndPushedIsFollowedAndTheDeliveredJobStaysInSightAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", Connections), ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }""")],
            [(".avala/permissions.json", Autonomous), (".avala/checks.json", """{ "checks": [{ "name": "git", "command": "git", "arguments": ["--version"] }] }""")],
            harnesses: true);
        await IdentityAsync(run);
        _ = await run.Repository.PublishAsync(Cancellation);
        var opened = run.Watch<PullRequestOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Plain("Run `git checkout -b fix/greeting`, create greet.py that prints 'Hello, world!', commit it, then run `git push origin fix/greeting`. Do not open a pull request."))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.Contains("fix/greeting", await run.Repository.GitInAsync(run.Repository.Remote, Cancellation, "branch", "--list", "fix/greeting"), StringComparison.Ordinal);

        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("fix/greeting", "main", "Add greet.py", Cancellation);
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Your pull request is open now. Reply with one word: OK.", Cancellation));
        var adopted = await opened.UntilAsync(found => found.Job == job);
        Assert.Equal((number, "fix/greeting"), (adopted.PullRequest.Number, adopted.PullRequest.Head));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        _ = await run.Get<IPullRequests>().RefreshAsync(job, Cancellation);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        await workbench.ShowsInGroupAsync(job, "Delivered", $"delivered · #{number} · checks 1 of 1");
        await run.Ui.InvokeAsync(() => workbench.Page.Execute("ToggleInspectorCommand"), Cancellation);
        await Camera.ShootAsync(run, "real-08-agents-pull-request-followed-and-delivered");
    }

    [Fact]
    public async Task TheDocsSiteIsARealJobThatReachesTheDocsFolderOnceMergedAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", Connections)],
            [(".avala/permissions.json", Autonomous), (".avala/jobs.json", $$"""{ "approval": "merge", "docs": { "enabled": true }, {{Research}} }"""), ("README.md", "# Greeter\n\nA tiny script that greets people.\n")],
            harnesses: true);
        await IdentityAsync(run);
        var docs = await DocsSettingAsync(run);
        await run.Ui.RunAsync(() => docs.ExecuteAsync("CreateSiteCommand"));
        Assert.False(Directory.Exists(Path.Combine(run.Repository.Path, "docs")));
        var job = Outcomes.Present((await run.Get<IDocumentation>().SiteAsync(run.Repository.Path, Cancellation)).Preparing);
        await Camera.ShootAsync(run, "real-09-docs-site-being-prepared-by-its-job", ToDocs);

        Assert.Equal(JobStatus.AwaitingReview, await LongSettledAsync(run, job));
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));

        Assert.True(File.Exists(Path.Combine(run.Repository.Path, "docs", "docfx.json")));
        Assert.True(File.Exists(Path.Combine(run.Repository.Path, "docs", "get-started", "quickstart.md")));
        _ = await DocsSettingAsync(run);
        await Camera.ShootAsync(run, "real-10-docs-site-in-the-folder-after-merge", ToDocs);
    }

    private static async Task<JobStatus> LongSettledAsync(SimulatedRun run, JobId job)
    {
        for (var quiet = 0; quiet < 20; quiet++)
        {
            var progressed = run.Watch<JobProgressed>();
            var status = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status;

            if (status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Failed)
            {
                return status;
            }

            try
            {
                return (await progressed.UntilAsync(found => found.Job == job && found.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Failed)).Status;
            }
            catch (TimeoutException)
            {
            }
        }

        return Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status;
    }

    private static void ToDocs(Avalonia.Controls.Window window)
    {
        var docs = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Control>().First(control => control.GetType().Name == "DocsSettingView");
        docs.BringIntoView(new Avalonia.Rect(docs.Bounds.Size));
    }

    private static string Plain(string instruction) => $"{instruction} Be brief.";

    private static async Task IdentityAsync(SimulatedRun run)
    {
        await run.Repository.GitAsync(Cancellation, "config", "user.name", "Ada");
        await run.Repository.GitAsync(Cancellation, "config", "user.email", "ada@localhost");
    }

    private static async Task<Bound> DocsSettingAsync(SimulatedRun run)
    {
        var page = run.Page("Settings");
        await run.Ui.RunAsync(() =>
        {
            run.Get<ShellViewModel>().SelectedPage = (IPage)page.Target;

            return page["Loading"].Value<Task>();
        });
        var settings = page["Repository"];
        await run.Ui.RunAsync(() => settings.ExecuteAsync("OpenCommand", run.Repository.Path));

        return await run.Ui.ReadAsync(() => settings["Docs"]);
    }

    private static void Gated()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        Assert.SkipUnless(HeadlessApp.Renders, "Set AVALA_HEADLESS_RENDER=1 so the run can take screenshots.");
    }
}
