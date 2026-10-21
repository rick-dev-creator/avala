using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class CrossHarnessDelegationTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string SimulatedClaudeCode = "Simulated Claude Code";

    private const string Delegate = "toolu_01CrossDelegate00000002";

    private const string Called = "toolu_01CrossCall0000000005";

    private static readonly (string File, string Content)[] TwoHarnesses =
    [
        ("connections.json", """
            {
              "default": "main",
              "connections": [
                { "name": "main", "provider": "simulator", "credential": { "source": "login" } },
                { "name": "second", "provider": "simulator-second", "credential": { "source": "login" } }
              ]
            }
            """),
        ("connections/main/.login", string.Empty),
        ("connections/second/.login", string.Empty),
    ];

    private static readonly string[] Lifecycle = ["working", "waiting", "completed", "failed", "stopped", "lost"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ConnectionName Main => new("main");

    [Fact]
    public async Task ACrossHarnessChildIsReportedToItsClaudeCodeOrchestratorAsACrossHarnessSubAgentNamingItsHarnessAndConnectionAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("delegate");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent migrate the database."),
            claude.Data,
            [(".avala/checks.json", PassingChecks), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""")]);
        var asked = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        _ = await run.ChildReportedAsync();
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));

        var report = Returned(await run.StopAndReadRecordingsAsync(), "toolu_01DelegateMigrate000002");

        Assert.Contains("\"connection\":\"sim\"", report, StringComparison.Ordinal);
        Assert.True(report.Contains(SimulatedClaudeCode, StringComparison.Ordinal), $"The orchestrator's report does not name the child's harness:\n{report}");
        Assert.True(report.Contains("\"kind\":\"crossHarness\"", StringComparison.Ordinal), $"The orchestrator's report does not say the child is a cross-harness sub-agent:\n{report}");
    }

    [Fact]
    public async Task ACrossHarnessChildShowsItsLifecycleInTheSameVocabularyAsAnInProcessSubAgentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoHarnesses,
            [(".avala/checks.json", PassingChecks), (".avala/jobs.json", """{ "delegation": { "connections": ["second"] } }""")]);
        var reported = run.Watch<ChildReported>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the orders database") { Connection = Main }));
        var asked = await run.DecisionAsync();
        var waiting = await ChildStateAsync(run, child => child["NeedsYou"].Value<bool>());

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        _ = await reported.UntilAsync(_ => true);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        var done = await ChildStateAsync(run, child => child["Activity"].Text == "integrated into its parent");

        Assert.True(Lifecycle.Contains(waiting, StringComparer.OrdinalIgnoreCase) && Lifecycle.Contains(done, StringComparer.OrdinalIgnoreCase), $"The child was shown as \"{waiting}\" then \"{done}\", outside the sub-agent lifecycle {string.Join(", ", Lifecycle)}");
        Assert.Equal(["waiting", "completed"], [waiting, done]);
    }

    [Fact]
    public async Task APermissionAskedByACrossHarnessChildIsAttributedToItsJobAndHarnessInTheClaudeCodeParentsConversationWhichLearnsItAwaitsPermissionAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("delegate-asks-parent");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a supervised sub-agent migrate the database and answer what it asks you."),
            claude.Data,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "connections": ["sim"], "escalation": "parent", "parentWindowSeconds": 600 } }"""),
            ]);
        var told = run.Watch<ParentAsked>();
        var asked = await told.UntilAsync(_ => true);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ChildAskingViewModel") is not null);
        var headline = Assert.Single(await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "ChildAskingViewModel", "Headline")));

        var early = Returned(await run.StopAndReadRecordingsAsync(), "toolu_01AskParentDelegate00002");

        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName(TranscribedClaude.Simulator)), asked.Delegation.Connection);
        Assert.True(headline.Contains(SimulatedClaudeCode, StringComparison.Ordinal), $"The parent's conversation does not name the asking child's harness: {headline}");
        Assert.True(early.Contains("\"state\":\"waiting\"", StringComparison.Ordinal), $"The parent was not told its child awaits permission:\n{early}");
    }

    [Fact]
    public async Task ACrossHarnessChildWhoseSessionIsLostIsReportedToItsParentAsLostWithTheReasonAndNotAsDoneAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-lost");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent migrate the database."),
            claude.Data,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""")]);
        var reported = Outcomes.Present((await run.ChildReportedAsync()).Report);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));

        var report = Returned(await run.StopAndReadRecordingsAsync(), Delegate);

        Assert.True(report.Contains("\"state\":\"lost\"", StringComparison.Ordinal), $"The parent was not told its child is lost:\n{report}");
        Assert.Contains("sessionLost", report, StringComparison.Ordinal);
        Assert.NotEqual(ChildOutcome.Integrated, reported.Outcome);
    }

    [Fact]
    public async Task AChildOnAnotherHarnessKeepsWorkingWhileItsParentsSessionEndsAndItsReportReachesTheParentWhenItResumesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoHarnesses,
            [(".avala/checks.json", PassingChecks), (".avala/jobs.json", """{ "delegation": { "connections": ["second"] } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("delegate-waiting")) { Connection = Main }));
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var asked = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IJobs>().HoldAsync(parent, HoldReason.Interrupted, Cancellation));

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        var reported = await run.ChildReportedAsync();
        Assert.True(reported.Answered.IsNone, "The report reached a parent whose session had ended");
        var progress = run.Watch<JobProgressed>();
        Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(parent, "Carry on with the release.", Cancellation));
        var delivered = await run.ReportDeliveredAsync();
        var told = await run.ToldAsync();
        _ = await progress.UntilAsync(update => update.Job == parent && update.Status == JobStatus.AwaitingReview);

        Assert.Equal((Option<JobId>.Some(child), ChildOutcome.Integrated, Option<ConnectionName>.Some(new ConnectionName("second"))), (reported.Child, Outcomes.Present(reported.Report).Outcome, reported.Connection));
        Assert.Equal(AnswerRoute.Message, Outcomes.Present(delivered.Answered).Route);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(told, $"\"job\":\"{child.Value}\""));
    }

    [Fact]
    public async Task ACrossHarnessChildAtItsUsageLimitIsReportedHeldWithTheReasonAndHandedOffToAnotherDelegationConnectionAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-limit");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent build the calculator."),
            WithSpareSimulator(claude.Data),
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["sim", "spare"] } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        _ = await run.ChildReportedAsync();
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        await run.DeliveredAsync();
        var handoffs = run.Get<IHandoffs>().OfJob(child);

        var told = Told(await run.StopAndReadRecordingsAsync());

        Assert.True(told.Any(text => text.Contains("limitNearlyReached", StringComparison.Ordinal)), $"The parent was never told its child is held at its limit:\n{string.Join("\n", told)}");
        var handoff = Assert.Single(handoffs);
        Assert.Equal(["sim", "spare"], new[] { handoff.From.Value, handoff.To.Value }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ADelegatedChildHeldAtItsLimitWithNoOtherConnectionGoesOnByItselfOnceItsWindowResetsAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-limit");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent build the calculator."),
            claude.Data,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var waits = run.Watch<JobWaitsForReset>();
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var wait = (await waits.UntilAsync(found => found.Wait.Job == child && found.Wait.ResumesAt.IsSome)).Wait;
        await run.DeliveredAsync();
        var progressed = run.Watch<JobProgressed>();

        run.Clock.Advance(Outcomes.Present(wait.ResumesAt) - run.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        var resumed = await progressed.UntilAsync(found => found.Job == child && found.Status != JobStatus.NeedsHelp);

        Assert.Equal(JobStatus.Running, resumed.Status);
    }

    [Fact]
    public async Task ADelegatedChildHeldAtItsLimitInARepositoryThatDeclaresLimitsGoesOnByItselfOnceItsWindowResetsAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-limit");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent build the calculator."),
            claude.Data,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] }, "limits": { "onLimit": "hold" } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var waits = run.Watch<JobWaitsForReset>();
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var wait = (await waits.UntilAsync(found => found.Wait.Job == child && found.Wait.ResumesAt.IsSome)).Wait;
        await run.DeliveredAsync();
        var progressed = run.Watch<JobProgressed>();

        run.Clock.Advance(Outcomes.Present(wait.ResumesAt) - run.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        var resumed = await progressed.UntilAsync(found => found.Job == child && found.Status != JobStatus.NeedsHelp);
        Assert.Equal(JobStatus.Running, resumed.Status);
    }

    [Fact]
    public async Task ADelegatedChildHeldAtItsLimitWhoseDelegationListsNoConnectionsGoesOnByItselfOnceItsWindowResetsAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-limit");
        var data = claude.Data.Select(entry =>
        {
            if (entry.File != "connections.json")
            {
                return entry;
            }

            var connections = JsonNode.Parse(entry.Content)!;
            connections["default"] = TranscribedClaude.Simulator;

            return (entry.File, connections.ToJsonString());
        }).ToList();
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent build the calculator.") { Connection = new ConnectionName(TranscribedClaude.Connection) },
            data,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "maxChildren": 2 } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var waits = run.Watch<JobWaitsForReset>();
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var wait = (await waits.UntilAsync(found => found.Wait.Job == child && found.Wait.ResumesAt.IsSome)).Wait;
        await run.DeliveredAsync();
        var progressed = run.Watch<JobProgressed>();

        run.Clock.Advance(Outcomes.Present(wait.ResumesAt) - run.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));

        var resumed = await progressed.UntilAsync(found => found.Job == child && found.Status != JobStatus.NeedsHelp);
        Assert.Equal(JobStatus.Running, resumed.Status);
    }

    [Fact]
    public async Task AClaudeCodeParentStopsItsCrossHarnessChildWhichEndsStoppedAndIsReportedStoppedAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-stop");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a supervised sub-agent migrate the database, then stop it."),
            claude.Data,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "connections": ["sim"], "escalation": "parent", "parentWindowSeconds": 600 } }"""),
            ]);
        var told = run.Watch<ParentAsked>();
        var child = Outcomes.Present((await told.UntilAsync(_ => true)).Delegation.Child);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        await run.DeliveredAsync();
        var status = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(child, Cancellation)).Summary.Status;

        var recordings = await run.StopAndReadRecordingsAsync();

        Assert.Equal(JobStatus.Discarded, status);
        Assert.Contains("\"state\":\"stopped\"", Returned(recordings, Called), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClaudeCodeOrchestratorAskingForAnUndeclaredConnectionIsRefusedWithAReasonNamingItAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-connection");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent on the elsewhere connection write the release notes."),
            claude.Data,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""")]);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        await run.DeliveredAsync();
        var children = await run.Get<IJobCatalog>().ChildrenAsync(run.Job, Cancellation);

        var refusal = Returned(await run.StopAndReadRecordingsAsync(), Delegate);

        Assert.Empty(children);
        Assert.Contains("\"refused\"", refusal, StringComparison.Ordinal);
        Assert.True(refusal.Contains("elsewhere", StringComparison.Ordinal) && !refusal.Contains("malformedInput", StringComparison.Ordinal), $"The refusal does not say the asked connection is undeclared:\n{refusal}");
    }

    [Fact]
    public async Task AClaudeCodeOrchestratorAskingForItsSubAgentsGetsInProcessAndCrossHarnessOnesTogetherEachWithItsKindAndStateAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("cross-harness-status");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Read the changelog with a sub-agent, have another migrate the database, then list your sub-agents."),
            claude.Data,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "connections": ["sim"], "escalation": "parent", "parentWindowSeconds": 600 } }"""),
            ]);
        var told = run.Watch<ParentAsked>();
        var child = Outcomes.Present((await told.UntilAsync(_ => true)).Delegation.Child);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        await run.DeliveredAsync();

        var listed = Returned(await run.StopAndReadRecordingsAsync(), Called);

        Assert.All(
            ["Read the changelog", "\"kind\":\"inProcess\"", "\"status\":\"completed\"", child.Value.ToString(), "\"kind\":\"crossHarness\"", "\"connection\":\"sim\"", "\"status\":\"awaitingPermission\""],
            expected => Assert.True(listed.Contains(expected, StringComparison.Ordinal), $"The listing lacks {expected}:\n{listed}"));
    }

    [Fact]
    public async Task ACrossHarnessChildWorksInItsOwnWorktreeAndItsAcceptedWorkReachesOnlyItsParentsBranchAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoHarnesses,
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["second"] } }""")]);
        var head = await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD");
        var started = run.Watch<JobSessionStarted>();
        var reported = run.Watch<ChildReported>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-across] Ship the release") { Connection = Main }));
        var child = (await started.UntilAsync(update => update.Job != parent)).Job;
        var childPath = (await WorkspaceAsync(run, child)).Path;
        var reports = await reported.CollectUntilAsync(update => update.Delegation.Item.Value == "delegate-todo");
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        var parentWorkspace = await WorkspaceAsync(run, parent);

        var log = await run.Repository.GitAsync(Cancellation, "log", "--format=%B", parentWorkspace.Branch);
        var status = await run.Repository.GitAsync(Cancellation, "status", "--porcelain");

        Assert.All(reports, report => Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("second")), report.Delegation.Connection));
        Assert.NotEqual(parentWorkspace.Path, childPath);
        Assert.False(childPath.StartsWith(run.Repository.Path, StringComparison.Ordinal), $"The child worked inside the main repository at {childPath}");
        Assert.Contains($"Avala-Job: {child.Value}", log, StringComparison.Ordinal);
        Assert.Equal("# Notes\n\nWritten by a delegated simulator.", await run.Repository.GitAsync(Cancellation, "show", $"{parentWorkspace.Branch}:NOTES.md"));
        Assert.Equal(head, await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD"));
        Assert.Equal(string.Empty, status);
        Assert.False(File.Exists(Path.Combine(run.Repository.Path, "NOTES.md")));
    }

    private static async Task<string> ChildStateAsync(SimulatedRun run, Func<Bound, bool> shown)
    {
        var overview = run.Page("Overview");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)overview.Target).Activate();

            return overview.Has("Loading") ? overview["Loading"].Value<Task>() : Task.CompletedTask;
        });
        var delegation = overview["Delegation"];
        await run.Ui.InvokeAsync(() => delegation.Choose("ChooseScopeCommand", "All"), Cancellation);
        await run.Ui.PresentedAsync(
            delegation.Presentation,
            () => delegation["Trees"].Items.Count > 0 && delegation["Trees"].Items[0]["Children"].Items.Count == 1 && shown(delegation["Trees"].Items[0]["Children"].Items[0]),
            () => string.Join(", ", delegation["Trees"].Items.SelectMany(tree => tree["Children"].Items).Select(child => $"{child["StateText"].Text} {child["Activity"].Text}")));

        return await run.Ui.ReadAsync(() => delegation["Trees"].Items[0]["Children"].Items[0]["StateText"].Text);
    }

    private static IReadOnlyList<(string File, string Content)> WithSpareSimulator(IReadOnlyList<(string File, string Content)> data)
    {
        var connections = JsonNode.Parse(data.Single(entry => entry.File == "connections.json").Content)!;
        connections["connections"]!.AsArray().Add(new JsonObject { ["name"] = "spare", ["provider"] = "simulator", ["credential"] = new JsonObject { ["source"] = "login" } });

        return [.. data.Select(entry => entry.File == "connections.json" ? (entry.File, connections.ToJsonString()) : entry), ("connections/spare/.login", string.Empty)];
    }

    private static string Returned(IReadOnlyList<string> recordings, string item) =>
        Entries(recordings).Select(entry => entry?["return"]).OfType<JsonNode>().FirstOrDefault(returned => returned["item"]?.GetValue<string>() == item)?["content"]?.GetValue<string>()
        ?? $"Nothing was returned to the call {item}.";

    private static IReadOnlyList<string> Told(IReadOnlyList<string> recordings) =>
        [.. Entries(recordings).Select(entry => entry?["return"]?["content"] ?? entry?["send"]?["text"]).OfType<JsonNode>().Select(text => text.GetValue<string>())];

    private static JsonArray Entries(IReadOnlyList<string> recordings) =>
        JsonNode.Parse(recordings.Single(text => text.Contains("\"claude-code\"", StringComparison.Ordinal)))!["entries"]!.AsArray();

    private static async Task<WorkspaceInfo> WorkspaceAsync(SimulatedRun run, JobId job)
    {
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation));
    }
}
