using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DelegationTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Connections = """
        {
          "default": "work",
          "connections": [
            { "name": "work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "personal", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private static readonly (string File, string Content)[] TwoLogins =
    [
        ("connections.json", Connections),
        ("connections/work/.login", string.Empty),
        ("connections/personal/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOrchestratorDelegatesToChildrenOnTwoConnectionsThatRunInParallelAndBringBackTheirVerifiedWorkAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoLogins,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "connections": ["work", "personal"], "routing": "roundRobin", "maxChildren": 2 } }"""),
            ]);
        var delegated = run.Watch<ChildDelegated>();
        var reported = run.Watch<ChildReported>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate] Ship the release")));

        var both = await delegated.CollectUntilAsync(started => started.Delegation.Item.Value == "delegate-todo");
        var reports = await reported.CollectUntilAsync(_ => true);
        reports = [.. reports, .. await reported.CollectUntilAsync(_ => true)];

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        Assert.Equal(["work", "personal"], both.Select(started => started.Delegation.Connection.Match(name => name.Value, () => string.Empty)));
        Assert.All(reports, report => Assert.Contains(both, started => started.Delegation.Child == report.Delegation.Child));
        Assert.True(
            both.Max(started => started.Delegation.At) <= reports.Min(report => Outcomes.Present(report.Delegation.Report).At),
            "A child was reported before its sibling was delegated, so they did not run at once");
        var tree = Outcomes.Present(await run.Get<IJobCatalog>().TreeAsync(job, Cancellation));
        Assert.Equal(
            [(JobStatus.Approved, "work"), (JobStatus.Approved, "personal")],
            tree.Children.Select(child => (child.Job.Status, child.Job.Connection.Match(name => name.Value, () => string.Empty))));
        Assert.All(reports.Select(report => Outcomes.Present(report.Delegation.Report)), report =>
        {
            Assert.Equal(ChildOutcome.Integrated, report.Outcome);
            Assert.Equal(VerificationOutcome.Passed, Outcomes.Present(report.Verification).Outcome);
            Assert.Equal("git", Assert.Single(Outcomes.Present(report.Verification).Checks).Name);
            Assert.Single(report.Files);
            Assert.NotEmpty(report.Spent);
        });
        var parent = Outcomes.Present(await WorkspaceOfAsync(run, job));
        Assert.Equal(["NOTES.md", "TODO.md"], (Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().DiffAsync(parent.Id, Cancellation))).Files.Select(file => file.Path));
        Assert.True(File.Exists(Path.Combine(parent.Path, "NOTES.md")));
        Assert.All(tree.Children, child => Assert.NotEmpty(run.Get<IPermissionAudit>().OfJob(child.Job.Job)));
    }

    [Fact]
    public async Task AChildInheritsItsParentsAutonomyAndCannotLoosenItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": {} }"""),
            ]);
        var refused = run.Watch<DelegationRefused>();
        var delegated = run.Watch<ChildDelegated>();
        var autonomies = run.Watch<AutonomyApplied>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(
            new JobRequest(string.Empty, "[simulate: delegate-loosen] Write the notes") { Autonomy = Enum.Parse<Autonomy>("Supervised") }));

        var refusal = (await refused.UntilAsync(_ => true)).Delegation;
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var applied = (await autonomies.UntilAsync(update => update.Autonomy.Job == child)).Autonomy;

        Assert.Equal(Option<DelegationError>.Some(DelegationError.AutonomyLoosened), refusal.Refusal);
        Assert.Equal(
            (Enum.Parse<Autonomy>("Autonomous"), Option<Autonomy>.Some(Enum.Parse<Autonomy>("Supervised")), Enum.Parse<Autonomy>("Supervised")),
            (applied.Declared, applied.Requested, applied.Effective));
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
    }

    [Fact]
    public async Task AChildCannotSpendMoreThanTheBudgetCarvedFromItsParentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/budget.json", """{ "costPerJob": { "USD": 1.00 }, "carvePerChild": 0.5 }"""),
                (".avala/jobs.json", """{ "delegation": {} }"""),
            ]);
        var carved = run.Watch<BudgetCarved>();
        var interventions = run.Watch<BudgetIntervened>();
        var reported = run.Watch<ChildReported>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-expensive] Rebuild the index")));

        var carve = (await carved.UntilAsync(_ => true)).Carve;
        var intervention = (await interventions.UntilAsync(_ => true)).Intervention;
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);

        Assert.Equal((job, new Cost(0.50m, "USD")), (carve.Parent, Assert.Single(carve.Cost)));
        Assert.Equal((carve.Child, HoldReason.BudgetExceeded), (intervention.Hold.Job, intervention.Hold.Reason));
        Assert.Equal(("USD", 0.60m, 0.50m), (intervention.Breach.Subject, intervention.Breach.Measured, intervention.Breach.Cap));
        Assert.Equal((ChildOutcome.Held, Option<HoldReason>.Some(HoldReason.BudgetExceeded)), (report.Outcome, report.Hold));
        Assert.Equal(Option<BudgetCarve>.Some(carve), report.Carve);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
    }

    [Fact]
    public async Task TheDepthCapStopsARecursiveDelegationAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "maxDepth": 1 } }"""),
            ]);
        var refused = run.Watch<DelegationRefused>();
        var reported = run.Watch<ChildReported>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: recursive] Delegate the work")));

        var refusal = (await refused.UntilAsync(_ => true)).Delegation;
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        var tree = Outcomes.Present(await run.Get<IJobCatalog>().TreeAsync(job, Cancellation));
        var child = Assert.Single(tree.Children);
        Assert.Empty(child.Children);
        Assert.Equal((Option<DelegationError>.Some(DelegationError.DepthExceeded), 2, Option<JobId>.Some(child.Job.Job)), (refusal.Refusal, refusal.Depth, refusal.Parent));
        Assert.Equal((ChildOutcome.Integrated, child.Job.Job), (report.Outcome, report.Child));
    }

    [Fact]
    public async Task AChildWhoseWorkConflictsWithItsParentIsReportedToTheParentAndWaitsForReviewAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": {} }"""),
            ]);
        var reported = run.Watch<ChildReported>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-conflict] Write the release notes")));

        var reports = await reported.CollectUntilAsync(_ => true);
        reports = [.. reports, .. await reported.CollectUntilAsync(_ => true)];

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        var outcomes = reports.Select(update => Outcomes.Present(update.Delegation.Report)).OrderBy(report => report.Outcome).ToList();
        Assert.Equal([ChildOutcome.Integrated, ChildOutcome.Conflict], outcomes.Select(report => report.Outcome));
        Assert.Equal(["NOTES.md"], outcomes[1].Conflicts);
        Assert.Equal(Option<JobRejection>.Some(JobRejection.MergeConflict), outcomes[1].Refusal);
        var waiting = Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation), child => child.Job == outcomes[1].Child);
        Assert.Equal(JobStatus.AwaitingReview, waiting.Status);
        Assert.Contains(
            run.Get<IDelegations>().OfParent(job),
            record => record.Report.Match(report => report.Outcome == ChildOutcome.Conflict, () => false));
    }

    [Fact]
    public async Task AnOrchestratorDelegatesByCapacityToAnotherHarnessAndASecondAccountWithCarvedBudgetsAndGetsTheirWorkAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [
                ("connections.json", """
                    {
                      "default": "main",
                      "connections": [
                        { "name": "main", "provider": "simulator", "credential": { "source": "login" } },
                        { "name": "second", "provider": "simulator-second", "credential": { "source": "login" } },
                        { "name": "spare", "provider": "simulator", "credential": { "source": "login" } }
                      ]
                    }
                    """),
                ("connections/main/.login", string.Empty),
                ("connections/second/.login", string.Empty),
                ("connections/spare/.login", string.Empty),
            ],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/budget.json", """{ "costPerJob": { "USD": 1.00 }, "carvePerChild": 0.5 }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["second", "spare"], "routing": "capacity" } }"""),
            ]);
        var delegated = run.Watch<ChildDelegated>();
        var reported = run.Watch<ChildReported>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(
            new JobRequest(string.Empty, "[simulate: delegate-across] Ship the release") { Connection = new ConnectionName("main") }));

        var both = await delegated.CollectUntilAsync(started => started.Delegation.Item.Value == "delegate-todo");
        var reports = await reported.CollectUntilAsync(update => update.Delegation.Item.Value == "delegate-todo");

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        Assert.Equal(["second", "spare"], both.Select(started => started.Delegation.Connection.Match(name => name.Value, () => string.Empty)));
        var choices = both.Select(started => Outcomes.Present(started.Delegation.Choice)).ToList();
        Assert.All(choices, choice => Assert.Equal(ChoiceReason.MostCapacity, choice.Reason));
        Assert.Equal(
            [("second", 0d), ("spare", 0d)],
            choices[0].Compared.Select(candidate => (candidate.Connection.Value, candidate.Used)));
        Assert.Contains(choices[1].Compared, candidate => candidate.Connection.Value == "second" && candidate.Used > 0);
        Assert.All(reports.Select(update => Outcomes.Present(update.Delegation.Report)), report =>
        {
            Assert.Equal(ChildOutcome.Integrated, report.Outcome);
            var carved = Assert.Single(Outcomes.Present(report.Carve).Cost);
            Assert.True(carved is { Currency: "USD", Amount: > 0m and <= 0.50m }, $"Carved {carved}");
        });
        var parent = Outcomes.Present(await WorkspaceOfAsync(run, job));
        Assert.Equal(["NOTES.md", "TODO.md"], Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().DiffAsync(parent.Id, Cancellation)).Files.Select(file => file.Path));
        Assert.Equal(
            ["Second simulated harness · second", "Simulated Claude Code · spare"],
            await HarnessesAsync(run, child => child["Activity"].Text == "integrated into its parent"));
    }

    [Fact]
    public async Task AClaudeCodeOrchestratorReplayedFromItsTranscriptDelegatesToASimulatedChildAndReceivesItsReportAsync()
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

        var reported = await run.ChildReportedAsync();
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        Assert.Equal(
            (Option<JobId>.Some(run.Job), Option<ConnectionName>.Some(new ConnectionName(TranscribedClaude.Simulator)), ChildOutcome.Integrated, AnswerRoute.ToolResult),
            (reported.Parent, reported.Connection, Outcomes.Present(reported.Report).Outcome, Outcomes.Present(reported.Answered).Route));
        Assert.Equal(["Simulated Claude Code · sim"], await HarnessesAsync(run, child => child["Activity"].Text == "integrated into its parent"));
        var recording = (await run.StopAndReadRecordingsAsync()).Single(text => text.Contains("\"claude-code\"", StringComparison.Ordinal));
        var returned = System.Text.Json.Nodes.JsonNode.Parse(recording)!["entries"]!.AsArray().Select(entry => entry?["return"]).OfType<System.Text.Json.Nodes.JsonNode>().Single();
        Assert.Contains($"\"job\":\"{Outcomes.Present(reported.Child).Value}\",\"outcome\":\"integrated\"", returned["content"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReportForAParentRunningALaterTurnArrivesMidTurnOnceWhenItsHarnessAcceptsMessagesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", PassingChecks), (".avala/jobs.json", """{ "delegation": {} }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-steered] Migrate the database")));
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var asked = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IJobs>().HoldAsync(job, HoldReason.Interrupted, Cancellation));
        var progress = run.Watch<JobProgressed>();
        Assert.Equal(ContinuedIn.SameSession, Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(job, "Carry on while it migrates.", Cancellation)).Conversation);

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));

        var delivered = await run.ReportDeliveredAsync();
        var queued = (MessageQueued)(await activity.UntilAsync(update => update.Event is MessageQueued)).Event;
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview);
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(queued.Text, $"\"job\":\"{child.Value}\""));
        Assert.Equal(Outcomes.Present(delivered.Answered), Outcomes.Present(Outcomes.Present(run.Get<IDelegations>().OfChild(child)).Answered));
    }

    private static async Task<IReadOnlyList<string>> HarnessesAsync(SimulatedRun run, Func<Bound, bool> settled)
    {
        var overview = run.Page("Overview");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)overview.Target).Activate();

            return overview.Has("Loading") ? overview["Loading"].Value<Task>() : Task.CompletedTask;
        });
        var delegation = overview["Delegation"];
        await run.Ui.PresentedAsync(
            delegation.Presentation,
            () => delegation["Children"].Items.Count > 0 && delegation["Children"].Items.All(settled),
            () => string.Join(", ", delegation["Children"].Items.Select(child => $"{child["Activity"].Text} {child["Harness"].Text}")));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
            [.. delegation["Children"].Items.Select(child => $"{child["Harness"].Text} · {child["Connection"].Text}").Order(StringComparer.Ordinal)]);
    }

    private static async Task<Option<WorkspaceInfo>> WorkspaceOfAsync(SimulatedRun run, JobId job) =>
        await (await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Bind(history => history.Summary.Workspace).Match(
            async workspace => (await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation)).Match(Option<WorkspaceInfo>.Some, _ => Option<WorkspaceInfo>.None),
            () => Task.FromResult(Option<WorkspaceInfo>.None));
}
