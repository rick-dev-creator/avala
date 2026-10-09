using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class GlobalPagesTests(PublishedPlugins plugins)
{
    private const string Connections = """
        {
          "default": "work",
          "connections": [
            { "name": "work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "personal", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private const string Autonomous = """{ "autonomy": "autonomous", "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test", "answer": "allow" } ] }""";

    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private static readonly (string File, string Content)[] TwoLogins =
    [
        ("connections.json", Connections),
        ("connections/work/.login", string.Empty),
        ("connections/personal/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOverviewListsTwoConnectionsWithTheAgentsRunningOnThemAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoLogins, []);
        _ = Outcomes.Succeeds(await run.SubmitAsync(Hanging("Fix the failing test", "work")));
        _ = Outcomes.Succeeds(await run.SubmitAsync(Hanging("Add an endpoint", "personal")));
        _ = await run.OpenedAsync();
        _ = await run.OpenedAsync();
        var connections = (await ActivatedAsync(run, "Overview"))["Connections"];

        await run.Ui.PresentedAsync(
            connections.Presentation,
            () => connections["Connections"].Items.Count == 2 && connections["Connections"].Items.All(card => card["Agents"].Items.Count == 1),
            () => string.Join(", ", connections["Connections"].Items.Select(card => $"{card["Name"].Text} with {card["Agents"].Items.Count} agents")));

        Assert.Equal(
            [("work", true, "Fix the failing test"), ("personal", false, "Add an endpoint")],
            await run.Ui.ReadAsync(() => connections["Connections"].Items
                .Select(card => (card["Name"].Text, card["IsDefault"].Value<bool>(), card["Agents"].Items[0]["Title"].Text.Split(" [")[0]))
                .ToList()));
    }

    [Fact]
    public async Task TheDelegationViewShowsTheChildrenOfASimulatedOrchestratorAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoLogins,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["work", "personal"], "routing": "roundRobin", "maxChildren": 2 } }"""),
            ]);
        var orchestrator = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate] Ship the release")));
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(orchestrator)));
        var delegation = (await ActivatedAsync(run, "Overview"))["Delegation"];

        await run.Ui.PresentedAsync(
            delegation.Presentation,
            () => delegation["Children"].Items.Count == 2 && delegation["Children"].Items.All(child => child["Activity"].Text == "integrated into its parent"),
            () => string.Join(", ", delegation["Children"].Items.Select(child => child["Activity"].Text)));

        Assert.Equal(orchestrator, await run.Ui.ReadAsync(() => delegation["Selected"]["Job"].Value<JobId>()));
        Assert.Equal(
            [("work", 1, true), ("personal", 1, true)],
            await run.Ui.ReadAsync(() => delegation["Children"].Items
                .Select(child => (child["Connection"].Text, child["Depth"].Value<int>(), child["Spent"].Text.EndsWith(" USD", StringComparison.Ordinal) && child["Harness"].Text.Length > 0))
                .ToList()));
    }

    [Fact]
    public async Task TheUsagePageShowsASimulatedJobsCostAndItsLimitAgainstTheHoldThresholdAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "spent-window", (".avala/budget.json", """{ "holdAtLimit": 0.9 }"""));
        Assert.Equal(HoldReason.LimitNearlyReached, (await run.BudgetInterventionAsync()).Hold.Reason);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var usage = await ActivatedAsync(run, "Usage");

        await run.Ui.PresentedAsync(
            usage.Presentation,
            () => usage["Connections"].Items.Count == 1
                && usage["Connections"].Items[0]["Limits"].Items.Count == 1
                && usage["Jobs"].Items.Count == 1
                && usage["Interventions"].Items.Count == 1,
            () => $"connections {usage["Connections"].Items.Count}, "
                + $"limits {(usage["Connections"].Items.Count > 0 ? usage["Connections"].Items[0]["Limits"].Items.Count : -1)}, "
                + $"jobs {usage["Jobs"].Items.Count}, interventions {usage["Interventions"].Items.Count}");

        var (cost, limit, job, intervention) = await run.Ui.ReadAsync(() => (
            usage["Connections"].Items[0]["Cost"].Text,
            usage["Connections"].Items[0]["Limits"].Items[0],
            usage["Jobs"].Items[0],
            usage["Interventions"].Items[0]));
        Assert.Equal("0.06 USD", cost);
        Assert.Equal(
            ("5h", "95%", "Jobs on this connection hold at the 90% threshold.", true, true),
            await run.Ui.ReadAsync(() => (limit["Window"].Text, limit["UsedText"].Text, limit["HoldAt"].Text, limit["ReachesHold"].Value<bool>(), limit["Resets"].Text.StartsWith("resets ", StringComparison.Ordinal))));
        Assert.Equal(
            ("0.06 USD", "holds at 90% of a limit", HoldReason.LimitNearlyReached),
            await run.Ui.ReadAsync(() => (job["Cost"].Text, job["Caps"].Text, intervention["Reason"].Value<HoldReason>())));
    }

    [Fact]
    public async Task TheSettingsShowARepositorysRulesReadFromItsCurrentCommitAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/permissions.json", Autonomous),
                (".avala/checks.json", PassingChecks),
                (".avala/jobs.json", """{ "approval": "merge" }"""),
            ]);
        var settings = (await ActivatedAsync(run, "Settings"))["Repository"];

        await run.Ui.RunAsync(() =>
        {
            settings.Set("Repository", run.Repository.Path);

            return settings.ExecuteAsync("ReadCommand");
        });

        Assert.Equal(
            (run.Repository.Path, "Autonomous", "tests", "Repository", "git --version", "approval", "merge"),
            await run.Ui.ReadAsync(() => (
                settings["Shown"].Text,
                settings["Autonomy"].Text,
                settings["Rules"].Items[2]["Name"].Text,
                settings["Rules"].Items[2]["Origin"].Text,
                Assert.Single(settings["Checks"].Items)["Command"].Text,
                settings["JobSections"].Items[0]["Name"].Text,
                settings["JobSections"].Items[0]["Value"].Text)));
        var files = await run.Ui.ReadAsync(() => settings["Files"].Items.Select(file => (file["Status"].Text, file["Commit"].Text)).ToList());
        Assert.Equal(["Applied", "Absent", "Applied", "Declared"], files.Select(file => file.Item1));
        Assert.Single(files.Select(file => file.Item2).Distinct(), commit => commit.Length == 7 && commit.All(char.IsAsciiHexDigitLower));
    }

    [Fact]
    public async Task TheResourcesPageShowsAnOrphanAndCleansItUpAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("processes"),
            [("resources.json", $$"""{ "orphans": "report", {{LeasedPorts.Settings(test: 0)}}, "worktrees": { "keepDiscardedHours": null } }""")],
            [(".avala/permissions.json", ServicesPolicy)]);
        var reaped = run.Watch<OrphansReaped>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var server = Assert.Single((await run.OrphansFoundAsync()).Processes);
        var resources = await ActivatedAsync(run, "Resources");
        await run.Ui.PresentedAsync(
            resources.Presentation,
            () => resources["Orphans"].Items.Count == 1,
            () => $"{resources["Orphans"].Items.Count} orphans");

        await run.Ui.RunAsync(() => resources.ExecuteAsync("ReapCommand", Assert.Single(resources["Orphans"].Items, orphan => orphan["CanReap"].Value<bool>()).Target));
        var killed = (await reaped.UntilAsync(_ => true)).Report;
        await run.Ui.PresentedAsync(
            resources.Presentation,
            () => resources["Orphans"].Items is [var orphan] && orphan["Disposal"].Text == "killed",
            () => string.Join(", ", resources["Orphans"].Items.Select(orphan => orphan["Disposal"].Text)));

        Assert.Equal((run.Job, OrphanDisposal.Killed, string.Empty), (Outcomes.Present(killed.Job), killed.Disposal, await run.Ui.ReadAsync(() => resources["Error"].Text)));
        Assert.True(await Workloads.IsGoneAsync(server.Id), $"Process {server.Id} survived its clean-up");
        Assert.Equal([OrphanDisposal.LeftRunning, OrphanDisposal.Killed], run.Get<IOrphans>().OfJob(run.Job).Select(report => report.Disposal));
    }

    [Fact]
    public async Task ANewJobSubmittedFromItsPageStartsOnTheChosenConnectionAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoLogins, []);
        var page = await ActivatedAsync(run, "New job");
        Assert.Contains("personal", await run.Ui.ReadAsync(() => page["Connections"].Items.Select(connection => connection.Text).ToList()));

        await run.Ui.RunAsync(() =>
        {
            page.Set("Repository", run.Repository.Path);
            page.Set("Instruction", SimulatedRun.Simulate("reply"));
            page.Set("Connection", "personal");

            return page.ExecuteAsync("SubmitCommand");
        });
        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var summary = Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation));
        Assert.Equal(("personal", string.Empty), (summary.Connection.Match(name => name.Value, () => string.Empty), await run.Ui.ReadAsync(() => page["Error"].Text)));
    }

    private static JobRequest Hanging(string title, string connection) =>
        new(string.Empty, $"{title} {SimulatedRun.Simulate("hang")}") { Connection = new Avala.Agents.Contracts.Connections.ConnectionName(connection) };

    private static async Task<Bound> ActivatedAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)page.Target).Activate();

            return page.Has("Loading") ? page["Loading"].Value<Task>() : Task.CompletedTask;
        });

        return page;
    }
}
