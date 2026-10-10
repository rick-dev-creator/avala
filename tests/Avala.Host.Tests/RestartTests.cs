using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RestartTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task TheLastEventsBeforeShutdownAreInTheTranscriptAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "waiting-permission");
        var asked = await run.DecisionAsync();

        await run.RestartAsync();
        await run.StartedAsync();

        var kept = await run.Get<Transcripts.Contracts.ITranscripts>().EarlierRunsAsync(run.Job, TestContext.Current.CancellationToken);
        Assert.Contains(kept, fact => fact.Fact is Transcripts.Contracts.PermissionRuled ruled && ruled.Decision.Item == asked.Item);
        Assert.Contains(kept, fact => fact.Fact is Transcripts.Contracts.AgentActed { Event: Agents.Contracts.Events.PermissionRequested requested } && requested.Item == asked.Item);
    }

    [Fact]
    public async Task AVerifiedJobShowsTheSameVerdictChecksAndTailsAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "fix-after-feedback",
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await EvidenceAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains("verdict: Verified on attempt 2 of 2", before);
        Assert.Contains(before, line => line.StartsWith("attempt: Attempt 1: failed · calculator failed (exit 1, ", StringComparison.Ordinal));
        Assert.Contains(before, line => line.StartsWith("exception: Attempt 1 failed | calculator · exit 1 | ", StringComparison.Ordinal));
        Assert.Equal(before, await EvidenceAsync(run));
    }

    [Fact]
    public async Task AJobCheckingWhenTheApplicationStopsRunsItsChecksAgainForTheSameAttemptWithoutAnotherAgentTurnAsync()
    {
        using var verdicts = new TcpListener(IPAddress.Loopback, 0);
        verdicts.Start();
        await using var run = await SimulatedRun.StartAsync(plugins, "reply", (".avala/checks.json", VerdictChecks(verdicts)));
        using (await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation))
        {
            await run.RestartAsync();
        }

        using var rerun = await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation);
        await rerun.GetStream().WriteAsync(new byte[] { 0 }, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));
        var attempt = Assert.Single(history.Attempts);
        Assert.Equal((AttemptOrigin.Initial, AttemptOutcome.Passed), (attempt.Origin, attempt.Outcome));
        Assert.Single(history.Sessions);
        var evidence = await EvidenceAsync(run);
        Assert.Contains("verdict: Verified on attempt 1 of 1", evidence);
        Assert.Contains("evidence: 1 of 1 check passed · Verified on attempt 1 of 1", evidence);
        Assert.StartsWith("attempt: Attempt 1: passed · verdict passed (exit 0, ", Assert.Single(evidence, line => line.StartsWith("attempt: ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal("Attempt 1", await run.Repository.GitInAsync(run.Worktree, Cancellation, "log", "--format=%s", "main..HEAD"));
    }

    [Fact]
    public async Task AJobCheckingAgainAfterARestartShowsTheSameSpendingDecisionsAndAutonomyInTheInspectorBeforeItsChecksEndAsync()
    {
        using var verdicts = new TcpListener(IPAddress.Loopback, 0);
        verdicts.Start();
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "governed",
            (".avala/checks.json", VerdictChecks(verdicts)),
            (".avala/permissions.json", GovernedPolicy));
        IReadOnlyList<string> before;

        using (await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation))
        {
            before = await InspectedWhileCheckingAsync(run);
            await run.RestartAsync();
        }

        using var rerun = await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation);
        var after = await InspectedWhileCheckingAsync(run);
        await rerun.GetStream().WriteAsync(new byte[] { 0 }, Cancellation);

        Assert.Contains(before, line => line.StartsWith("usage: USD 0.01", StringComparison.Ordinal));
        Assert.Contains(before, line => line.StartsWith("audit: 1 denied", StringComparison.Ordinal));
        Assert.Contains("autonomy: Autonomous · Autonomous, as the repository declares", before);
        Assert.Equal(before, after);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    private static async Task<IReadOnlyList<string>> InspectedWhileCheckingAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var sections = await workbench.InspectAsync(run.Job, "UsageSectionViewModel", "AuditSectionViewModel", "AutonomySectionViewModel");
        var (usage, audit, autonomy) = (sections[0], sections[1], sections[2]);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"usage: {usage["Fact"].Text} · {usage["Spent"].Text}",
            $"audit: {audit["Fact"].Text} · {audit["Summary"].Text}",
            .. audit["Decisions"].Value<IReadOnlyList<string>>().Select(decision => $"decision: {decision}"),
            $"autonomy: {autonomy["Fact"].Text} · {autonomy["Autonomy"].Text}",
        ]);
    }

    [Fact]
    public async Task TheAuditOfAGovernedJobShowsTheSameDecisionsDenialsAssumptionsAndAutonomyAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "governed", (".avala/permissions.json", GovernedPolicy));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await AuditAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains(before, line => line.StartsWith("audit: 1 denied · ", StringComparison.Ordinal) && line.EndsWith("1 assumption", StringComparison.Ordinal));
        Assert.Contains("decision: Denied Command dotnet ef database update · rule no-migrations", before);
        Assert.Contains("assumption: Which database should the service use?: PostgreSQL", before);
        Assert.Contains("autonomy: Autonomous · Autonomous, as the repository declares", before);
        Assert.Contains(before, line => line.StartsWith("exception: Denied: run dotnet ef database update", StringComparison.Ordinal));
        Assert.Equal(before, await AuditAsync(run));
    }

    [Fact]
    public async Task AJobsCapNearLimitAlertAndAccountShowTheSameOnUsageOverviewAndInspectorAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "spent-window",
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.065 }, "holdAtLimit": 0.9 }"""));
        Assert.Equal(HoldReason.LimitNearlyReached, (await run.BudgetInterventionAsync()).Hold.Reason);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var before = await SpendingAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains("job: 0.06 USD of 0.065 USD · near cap True · 0.065 USD per job, holds at 90% of a limit · on simulator", before);
        Assert.Contains("limit: 5h 95% · Jobs on this connection hold at the 90% threshold. · reaches hold True", before);
        Assert.Contains("card: simulator · Simulated account · 5h · 95% · near limit True", before);
        Assert.Contains(before, line => line.StartsWith("inspector: ", StringComparison.Ordinal) && line.Contains("Cost cap", StringComparison.Ordinal));
        Assert.Equal(before, await SpendingAsync(run));
    }

    [Fact]
    public async Task TheReasonAndComparedReadingsOfAConnectionChosenByCapacityShowTheSameAfterARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, [(".avala/budget.json", """{ "holdAtLimit": 0.9 }""")]);
        var recorded = run.Watch<UsageRecorded>();
        var near = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("near-limit")) { Connection = new ConnectionName("simulator-one") }));
        var reports = 0;
        _ = await recorded.UntilAsync(_ => ++reports == 2);
        _ = await run.SettledAsync(near);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var before = await ChoiceAsync(run, job);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Equal(
            [
                "connection: simulator-two · Chosen by capacity: simulator-two had the most left",
                "compared: simulator-one · 95% of 5h · holds at 90% · at its limit · chosen False",
                "compared: simulator-two · no usage reported · holds at 90% · chosen True",
            ],
            before);
        Assert.Equal(before, await ChoiceAsync(run, job));
    }

    [Fact]
    public async Task AnOrchestratorsChildrenOutcomesHarnessAndRootCapShowTheSameAfterARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [
                (".avala/checks.json", """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }"""),
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/budget.json", """{ "costPerJob": { "USD": 1.00 }, "carvePerChild": 0.5 }"""),
                (".avala/jobs.json", """{ "delegation": { "maxChildren": 2 } }"""),
            ]);
        var orchestrator = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate] Ship the release")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(orchestrator));
        var before = await DelegationAsync(run, orchestrator);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains(before, line => line.StartsWith("root: ", StringComparison.Ordinal) && line.Contains("Simulated Claude Code · simulator · Budget 1 USD", StringComparison.Ordinal));
        Assert.Equal(2, before.Count(line => line.StartsWith("child: ", StringComparison.Ordinal) && line.Contains("integrated into its parent · Simulated Claude Code", StringComparison.Ordinal)));
        Assert.Equal(2, before.Count(line => line.StartsWith("inspected: ", StringComparison.Ordinal) && line.EndsWith("Approved · simulator · Integrated", StringComparison.Ordinal)));
        Assert.Equal(before, await DelegationAsync(run, orchestrator));
    }

    private static async Task<IReadOnlyList<string>> DelegationAsync(SimulatedRun run, JobId orchestrator)
    {
        var delegation = (await ActivatedAsync(run, "Overview"))["Delegation"];
        await run.Ui.PresentedAsync(
            delegation.Presentation,
            () => delegation["Children"].Items.Count == 2 && delegation.Has("Root"),
            () => string.Join(", ", delegation["Children"].Items.Select(child => child["Activity"].Text)));
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(orchestrator, "ReadyForReview");
        var section = Assert.Single(await workbench.InspectAsync(orchestrator, "DelegationSectionViewModel"));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        {
            var root = delegation["Root"];

            return
            [
                $"root: {root["Title"].Text} ·{root["Harness"].Text} · {root["Connection"].Text} · {root["Budget"].Text} · {root["Spent"].Text} · {root["ShareNote"].Text}",
                .. delegation["Children"].Items
                    .Select(child => $"child: {child["Title"].Text} · {child["Activity"].Text} · {child["Harness"].Text} · {child["Connection"].Text} · {child["Spent"].Text} · {child["Carve"].Text}")
                    .Order(StringComparer.Ordinal),
                $"refused: {delegation["Refused"].Items.Count}",
                $"inspector: {section["Fact"].Text}",
                .. section["Children"].Value<IReadOnlyList<string>>().Select(child => $"inspected: {child}").Order(StringComparer.Ordinal),
            ];
        });
    }

    [Fact]
    public async Task AReadingWhoseWindowResetIsShownAsResetLiveAndAfterARestartAndCapacityTreatsItsConnectionAsFreshAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, [(".avala/budget.json", """{ "holdAtLimit": 0.9 }""")]);
        var recorded = run.Watch<UsageRecorded>();
        var near = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("near-limit")) { Connection = new ConnectionName("simulator-one") }));
        var reports = 0;
        _ = await recorded.UntilAsync(_ => ++reports == 2);
        _ = await run.SettledAsync(near);
        var usage = await ActivatedAsync(run, "Usage");
        await run.Ui.PresentedAsync(usage.Presentation, () => LimitOf(usage) == "95%", () => $"limit {LimitOf(usage)}");

        run.Clock.Advance(TimeSpan.FromSeconds(3));

        await run.Ui.PresentedAsync(usage.Presentation, () => LimitOf(usage) == "reset", () => $"limit {LimitOf(usage)}");
        Assert.Equal(("5h · reset", false), await CardAsync(run));

        await run.RestartAsync();
        await run.StartedAsync();

        var restarted = await ActivatedAsync(run, "Usage");
        await run.Ui.PresentedAsync(restarted.Presentation, () => LimitOf(restarted) == "reset", () => $"limit {LimitOf(restarted)}");
        Assert.Equal(("5h · reset", false), await CardAsync(run));
        var fresh = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(fresh));
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(fresh, TestContext.Current.CancellationToken));
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("simulator-one")), history.Summary.Connection);
        Assert.All(Outcomes.Present(history.Choice).Compared, candidate => Assert.Equal((0d, true), (candidate.Used, candidate.Available)));
    }

    [Fact]
    public async Task APermissionPendingWhenTheApplicationStopsIsNoLongerOfferedAfterARestartAndItsAuditSaysItsSessionEndedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "waiting-permission");
        Assert.Equal(Permissions.Contracts.DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await run.ResumableAsync();
        var before = await run.WorkbenchAsync();
        var pending = await run.Ui.ReadAsync(() => before.Toolbar["Decisions"]);
        await before.ShowsAsync(() => pending["Items"].Items.Count == 1);

        await run.RestartAsync();

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var after = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => after.Toolbar["Decisions"]);
        await after.ShowsInGroupAsync(run.Job, "ReadyForReview");
        Assert.Equal((true, 0), await run.Ui.ReadAsync(() => (decisions["IsEmpty"].Value<bool>(), decisions["Items"].Items.Count)));
        var audit = Assert.Single(await after.InspectAsync(run.Job, "AuditSectionViewModel"));
        Assert.Contains(
            "Asked you Command dotnet ef database update · unanswered, its session ended",
            await run.Ui.ReadAsync(() => audit["Decisions"].Value<IReadOnlyList<string>>()));
    }

    [Fact]
    public async Task DontAskAgainForThisJobAnswersTheSameRequestInTheNewSessionARestartAndARetryOpenAsync()
    {
        using var verdicts = new TcpListener(IPAddress.Loopback, 0);
        verdicts.Start();
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/checks.json", VerdictChecks(verdicts)));
        var first = await run.DecisionAsync();
        var answer = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(
            first.Session,
            new PermissionReply(first.Item, Agents.Contracts.Events.PermissionAnswer.Allow) { Remember = Remember.ForThisJob },
            Cancellation));

        using (await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation))
        {
            await run.RestartAsync();
        }

        using (var failing = await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation))
        {
            await failing.GetStream().WriteAsync(new byte[] { 1 }, Cancellation);
        }

        var again = await run.DecisionAsync();
        using var passing = await verdicts.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation);
        await passing.GetStream().WriteAsync(new byte[] { 0 }, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var rule = Outcomes.Present(answer.Rule);
        Assert.Equal((RuleOrigin.Job, "don't ask again for this job", "dotnet ef database update"), (rule.Origin, rule.Name, Outcomes.Present(rule.Target)));
        Assert.NotEqual(first.Session, again.Session);
        Assert.Equal((first.Target, PolicyAnswer.Allow, DecisionDelivery.Answered, Option<PolicyRule>.Some(rule)), (again.Target, again.Answer, again.Delivery, again.Rule));
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));
        Assert.Equal([AttemptOrigin.Initial, AttemptOrigin.Retry], history.Attempts.Select(attempt => attempt.Origin));
        Assert.Equal([rule], run.Get<IPermissionAudit>().JobRulesOf(run.Job));
    }

    private static string LimitOf(Bound usage) =>
        usage["Connections"].Items.Where(connection => connection["Name"].Text == "simulator-one").SelectMany(connection => connection["Limits"].Items).Select(limit => limit["UsedText"].Text).FirstOrDefault() ?? "none";

    private static async Task<(string Use, bool IsNearLimit)> CardAsync(SimulatedRun run)
    {
        var overview = (await ActivatedAsync(run, "Overview"))["Connections"];
        await run.Ui.PresentedAsync(
            overview.Presentation,
            () => overview["Connections"].Items.Any(card => card["Name"].Text == "simulator-one"),
            () => $"cards {overview["Connections"].Items.Count}");

        return await run.Ui.ReadAsync(() =>
        {
            var card = overview["Connections"].Items.Single(found => found["Name"].Text == "simulator-one");

            return (card["Use"].Text, card["IsNearLimit"].Value<bool>());
        });
    }

    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private static async Task<IReadOnlyList<string>> ChoiceAsync(SimulatedRun run, JobId job)
    {
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(job, "ReadyForReview");
        var section = Assert.Single(await workbench.InspectAsync(job, "AutonomySectionViewModel"));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"connection: {section["Connection"].Text} · {section["Reason"].Text}",
            .. section["Compared"].Items.Select(line => $"compared: {line["Connection"].Text} · {line["Reading"].Text} · chosen {line["IsChosen"].Text}"),
        ]);
    }

    private static async Task<IReadOnlyList<string>> SpendingAsync(SimulatedRun run)
    {
        var usage = await ActivatedAsync(run, "Usage");
        await run.Ui.PresentedAsync(
            usage.Presentation,
            () => usage["Jobs"].Items.Count == 1 && usage["Connections"].Items.Count == 1 && usage["Connections"].Items[0]["Limits"].Items.Count == 1,
            () => $"jobs {usage["Jobs"].Items.Count}, connections {usage["Connections"].Items.Count}");
        var overview = (await ActivatedAsync(run, "Overview"))["Connections"];
        await run.Ui.PresentedAsync(
            overview.Presentation,
            () => overview["Connections"].Items.Count == 1,
            () => $"cards {overview["Connections"].Items.Count}");
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(run.Job, "NeedsYou");
        var inspector = Assert.Single(await workbench.InspectAsync(run.Job, "UsageSectionViewModel"));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        {
            var (job, limit, card) = (usage["Jobs"].Items[0], usage["Connections"].Items[0]["Limits"].Items[0], overview["Connections"].Items[0]);

            return
            [
                $"job: {job["Cost"].Text} of {job["Cap"].Text} · near cap {job["IsNearCap"].Text} · {job["Caps"].Text} · on {job["Connection"].Text}",
                $"limit: {limit["Window"].Text} {limit["UsedText"].Text} · {limit["HoldAt"].Text} · reaches hold {limit["ReachesHold"].Text}",
                $"card: {card["Name"].Text} · {card["Account"].Text} · {card["Use"].Text} · near limit {card["IsNearLimit"].Text}",
                $"inspector: {inspector["Spent"].Text} · {string.Join(", ", inspector["Caps"].Value<IReadOnlyList<string>>())} · {string.Join(", ", inspector["Interventions"].Value<IReadOnlyList<string>>())}",
            ];
        });
    }

    private static async Task<Bound> ActivatedAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.RunAsync(() =>
        {
            ((Avala.Sdk.IActivatable)page.Target).Activate();

            return page.Has("Loading") ? page["Loading"].Value<Task>() : Task.CompletedTask;
        });

        return page;
    }

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string VerdictChecks(TcpListener verdicts) =>
        JsonSerializer.Serialize(new
        {
            checks = new[]
            {
                new
                {
                    name = "verdict",
                    command = "dotnet",
                    arguments = new[] { Workloads.Program, "verdict", ((IPEndPoint)verdicts.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) },
                },
            },
        });

    private const string GovernedPolicy = """
        { "autonomy": "autonomous", "rules": [ { "name": "no-migrations", "kind": "command", "target": "dotnet ef*", "answer": "deny" } ] }
        """;

    private static async Task<IReadOnlyList<string>> AuditAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
        var sections = await workbench.InspectAsync(run.Job, "AuditSectionViewModel", "AutonomySectionViewModel");
        var (audit, autonomy) = (sections[0], sections[1]);
        var review = await ReviewAsync(run, workbench);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"audit: {audit["Fact"].Text} · {audit["Summary"].Text}",
            .. audit["Decisions"].Value<IReadOnlyList<string>>().Select(decision => $"decision: {decision}"),
            .. audit["Assumptions"].Value<IReadOnlyList<string>>().Select(assumption => $"assumption: {assumption}"),
            $"autonomy: {autonomy["Fact"].Text} · {autonomy["Autonomy"].Text}",
            .. review["Exceptions"].Items.Select(exception => $"exception: {exception["Title"].Text} | {exception["Fact"].Text}"),
        ]);
    }

    private static async Task<IReadOnlyList<string>> EvidenceAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var (group, fact) = await workbench.RowAsync(run.Job, "ReadyForReview");
        var evidence = Assert.Single(await workbench.InspectAsync(run.Job, "EvidenceSectionViewModel"));
        var review = await ReviewAsync(run, workbench);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"row: {group} · {fact}",
            $"evidence: {evidence["Fact"].Text} · {evidence["Summary"].Text}",
            .. evidence["Attempts"].Value<IReadOnlyList<string>>().Select(attempt => $"attempt: {attempt}"),
            $"verdict: {review["Verdict"].Text}",
            .. review["Exceptions"].Items.Select(exception => $"exception: {exception["Title"].Text} | {exception["Fact"].Text} | {exception["Output"].Text}"),
        ]);
    }

    private static async Task<Bound> ReviewAsync(SimulatedRun run, OpenWorkbench workbench)
    {
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));

        return await run.Ui.ReadAsync(() => workbench.Page["Review"]);
    }
}
