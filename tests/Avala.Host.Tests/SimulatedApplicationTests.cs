using System.Runtime.Loader;
using System.Xml.Linq;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed class SimulatedApplicationTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePublishedPluginsComposeEveryModuleWithOneIdentityPerAssemblyAsync()
    {
        using var data = new TemporaryFolder();
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));

        Assert.NotNull(root.Services.GetRequiredService<IAgents>());
        Assert.NotNull(root.Services.GetRequiredService<IWorkspaces>());
        Assert.NotNull(root.Services.GetRequiredService<IJobs>());
        Assert.NotNull(root.Services.GetRequiredService<IUsage>());
        Assert.NotNull(root.Services.GetRequiredService<IVerifications>());
        Assert.NotNull(root.Services.GetRequiredService<IPermissionAudit>());
        Assert.NotNull(root.Services.GetRequiredService<ISupervision>());
        Assert.NotNull(root.Services.GetRequiredService<IBudgets>());
        Assert.Equal("simulator", Assert.Single(root.Services.GetServices<IAgentProvider>()).Info.Id);
        Assert.Empty(AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name?.StartsWith("Avala.", StringComparison.Ordinal) == true)
            .GroupBy(name => name)
            .Where(copies => copies.Count() > 1)
            .Select(copies => copies.Key));
    }

    [Fact]
    public async Task AReplyJobAwaitsReviewAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task EveryActionOfAnEditJobMeetsThePolicyAndTheEditLandsInTheWorktreeAndInItsCheckpointAsync()
    {
        const string Greeting = "# Hello\n\nWritten by the simulator.\n";
        await using var run = await SimulatedRun.StartAsync(plugins, "edit", (".avala/permissions.json", TestsPolicy));

        var decisions = await run.DecisionsAsync(count: 2);

        Assert.Equal(
            [
                ("edits-inside-the-workspace", ItemKind.FileEdit, "GREETING.md", PolicyAnswer.Allow, DecisionDelivery.Answered),
                ("tests", ItemKind.Command, "dotnet test", PolicyAnswer.Allow, DecisionDelivery.Answered),
            ],
            decisions.Select(decision => (Outcomes.Present(decision.Rule).Name, decision.Kind, decision.Target, decision.Answer, decision.Delivery)));
        Assert.Equal(decisions, run.Get<IPermissionAudit>().OfJob(run.Job));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal(Greeting, await File.ReadAllTextAsync(Path.Combine(run.Worktree, "GREETING.md"), Cancellation));
        Assert.Equal("Attempt 1", await run.Repository.GitInAsync(run.Worktree, Cancellation, "log", "-1", "--format=%s"));
        Assert.Equal(Greeting.Trim(), await run.Repository.GitInAsync(run.Worktree, Cancellation, "show", "HEAD:GREETING.md"));
    }

    [Fact]
    public async Task ACrashingAgentHoldsItsJobAsSessionLostAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "crash");

        var hold = await run.HoldAsync();

        Assert.Equal((run.Job, HoldReason.SessionLost, SessionHalt.Stopped), (hold.Job, hold.Reason, hold.Halt));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
    }

    [Fact]
    public async Task AHangingAgentIsInterruptedAndItsJobHeldAsStalledAsync()
    {
        var window = TimeSpan.FromSeconds(1);
        await using var run = await SimulatedRun.SupervisedAsync(plugins, "hang", window);

        await run.SilentForAsync(window);

        var turn = await run.TurnAsync();

        Assert.Equal(TurnOutcome.Interrupted, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var hold = await run.SupervisorInterventionAsync();
        Assert.Equal([hold], run.Get<ISupervision>().OfJob(run.Job));
        Assert.Equal((HoldReason.Stalled, SessionHalt.Interrupted, window), (hold.Hold.Reason, hold.Hold.Halt, hold.Silence.Window));
        Assert.True(hold.Silence.Silent >= window, $"Held after {hold.Silence.Silent} of silence");
    }

    [Fact]
    public async Task AJobThatSpendsItsBudgetIsHeldWithWhatItSpentAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/budget.json", """{ "costPerJob": { "USD": 0.01 } }"""));

        var hold = await run.BudgetInterventionAsync();

        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        Assert.Equal([hold], run.Get<IBudgets>().OfJob(run.Job));
        Assert.Equal(HoldReason.BudgetExceeded, hold.Hold.Reason);
        Assert.Equal(new BudgetBreach(BudgetMeasure.Cost, "USD", 0.0110m, 0.01m, Option<BudgetError>.None), hold.Breach);
    }

    [Fact]
    public async Task AJobWhoseProviderLimitPassesTheThresholdIsInterruptedAndHeldAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/budget.json", """{ "holdAtLimit": 0.25 }"""));

        var turn = await run.TurnAsync();

        Assert.Equal(TurnOutcome.Interrupted, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var hold = await run.BudgetInterventionAsync();
        Assert.Equal([hold], run.Get<IBudgets>().OfJob(run.Job));
        Assert.Equal((HoldReason.LimitNearlyReached, SessionHalt.Interrupted), (hold.Hold.Reason, hold.Hold.Halt));
        Assert.Equal(new BudgetBreach(BudgetMeasure.Limit, "5h", 0.30m, 0.25m, Option<BudgetError>.None), hold.Breach);
    }

    [Fact]
    public async Task AnItemLeftOpenIsAbandonedBeforeTheTurnFinishesAndTheJobAwaitsReviewAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "left-open");

        var turn = await run.TurnAsync();

        Assert.Equal(TurnOutcome.Finished, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
        Assert.Contains(turn.SkipLast(1), update => update is ItemCompleted { Outcome: ItemOutcome.Abandoned, Item.Value: "build" });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Empty(run.Get<ISupervision>().OfJob(run.Job));
    }

    [Fact]
    public async Task AJobRecoveredAfterARestartResumesItsSimulatedConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        await run.ResumableAsync();

        await run.RestartAsync();

        var turn = await run.TurnAsync();
        Assert.Contains(turn, update => update is ItemProgressed { Text: "Finished what I was doing." });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AJobWhoseSessionWasLostResumesItsConversationWhenAHumanContinuesItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "crash");
        Assert.Equal(HoldReason.SessionLost, (await run.HoldAsync()).Reason);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());

        var continued = Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(run.Job, "Please finish the work.", Cancellation));

        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task CanvasesDrawnThroughTheInjectedCanvasToolArriveInOrderAsSnapshotsThatEndCompleteAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "canvas");

        var snapshots = await run.CanvasSnapshotsAsync(canvasCount: 3);

        Assert.Equal("svg", XDocument.Parse(Canvas(snapshots, "diagram", "image/svg+xml")).Root?.Name.LocalName);
        Assert.Equal("svg", XDocument.Parse(Canvas(snapshots, "flow", "image/svg+xml")).Root?.Name.LocalName);
        Assert.Equal(
            "# The host and its plugins\n\n- The host knows no module.\n- Every module is a **plugin**.\n",
            Canvas(snapshots, "notes", "text/markdown"));
        Assert.Equal(
            [("diagram", CanvasStatus.Completed, true), ("flow", CanvasStatus.Completed, true), ("notes", CanvasStatus.Completed, true)],
            run.Canvases.InSession(snapshots[0].Session).Select(canvas => (canvas.Canvas.Item.Value, canvas.Status, canvas.IsOffered)));
    }

    [Fact]
    public async Task UsageWithCostAndAUsageLimitReachTheBusAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        var turn = await run.TurnAsync();

        var usage = Assert.Single(turn.OfType<UsageReported>());
        Assert.True(usage.Tokens.Input > 0 && usage.Tokens.Output > 0);
        Assert.True(usage.Cost.Match(cost => cost is { Amount: > 0, Currency: "USD" }, () => false));
        Assert.InRange(Assert.Single(turn.OfType<LimitReported>()).Limit.UsedFraction, double.Epsilon, 1);
    }

    [Fact]
    public async Task ASimulatedJobShowsItsUsageCostAndLimitInTheAggregatesAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.UsageRecordedAsync(reports: 2);

        var usage = run.Get<IUsage>();
        var job = Outcomes.Present(usage.OfJob(run.Job));
        Assert.Equal(new TokenUsage(1_200, 80, 600, 120, 20), job.Tokens);
        Assert.Equal([new Cost(0.0042m, "USD")], job.Costs);
        Assert.Equal(1, job.Turns.Finished);
        Assert.Equal([new UsageLimit("5h", 0.12, Option<DateTimeOffset>.None)], job.Limits);
        var provider = Assert.Single(usage.ByProvider());
        Assert.Equal("simulator", provider.Provider.Id);
        Assert.Equal(job.Tokens, provider.Usage.Tokens);
        var account = Assert.Single(usage.ByAccount());
        Assert.Equal(("simulator", "simulated-account", job.Tokens), (account.Provider.Id, account.Account.Id, account.Usage.Tokens));
    }

    [Fact]
    public async Task AJobReachesReviewOnlyAfterItsDeclaredCheckPassesWithTheEvidenceOfEveryAttemptAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "fix-after-feedback",
            (".avala/checks.json", CalculatorChecks),
            (".avala/permissions.json", TestsPolicy));

        var journey = await run.JourneyAsync();

        Assert.Equal(
            [JobStatus.Running, JobStatus.Checking, JobStatus.Running, JobStatus.Checking, JobStatus.AwaitingReview],
            journey.SkipWhile(status => status != JobStatus.Running));
        var reports = run.Get<IVerifications>().OfJob(run.Job);
        Assert.Equal(
            ["1 Failed Retry: git Passed 0, calculator Failed 1", "2 Passed Pass: git Passed 0, calculator Passed 0"],
            reports.Select(report => $"{report.Attempt} {report.Outcome} {report.Verdict.Decision}: "
                + string.Join(", ", report.Checks.Select(check => $"{check.Name} {check.Status} {check.ExitCode.Match(code => code, () => -1)}"))));
        Assert.StartsWith("git version", reports[0].Checks[0].OutputTail, StringComparison.Ordinal);
        Assert.Contains(
            "\"calculator\" did not pass: `git grep --quiet --fixed-strings \"add(2, 2) = 4\" -- calculator.txt` exited with code 1",
            reports[0].Verdict.Feedback,
            StringComparison.Ordinal);
        Assert.Equal("add(2, 2) = 4\n", await File.ReadAllTextAsync(Path.Combine(run.Worktree, "calculator.txt"), Cancellation));
    }

    [Fact]
    public async Task AnAgentThatEmptiesTheCheckDeclarationIsStillVerifiedByTheChecksOfItsBaseCommitAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "rewrite-checks",
            (".avala/checks.json", CalculatorChecks),
            (".avala/permissions.json", TestsPolicy));
        var baseCommit = await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD");

        var journey = await run.JourneyAsync();

        Assert.Equal(
            [JobStatus.Running, JobStatus.Checking, JobStatus.Running, JobStatus.Checking, JobStatus.AwaitingReview],
            journey.SkipWhile(status => status != JobStatus.Running));
        var reports = run.Get<IVerifications>().OfJob(run.Job);
        Assert.Equal(
            ["1 Failed: git Passed, calculator Failed", "2 Passed: git Passed, calculator Passed"],
            reports.Select(report => $"{report.Attempt} {report.Outcome}: " + string.Join(", ", report.Checks.Select(check => $"{check.Name} {check.Status}"))));
        Assert.All(reports, report => Assert.Equal(Option<FileOrigin>.Some(new FileOrigin(baseCommit, EditedInWorktree: true)), report.Declaration));
        Assert.Contains("Your changes to .avala/checks.json do not apply to this job", reports[0].Verdict.Feedback, StringComparison.Ordinal);
        Assert.Equal("{ \"checks\": [] }\n", await File.ReadAllTextAsync(Path.Combine(run.Worktree, ".avala", "checks.json"), Cancellation));
    }

    [Fact]
    public Task AnAllowingPolicyLetsThePermissionJobRunItsCommandAndFinishUnattendedAsync() =>
        AnsweredByPolicyAsync("allow", PolicyAnswer.Allow, ItemOutcome.Succeeded);

    [Fact]
    public Task ADenyingPolicyCancelsTheCommandAndTheJobFinishesUnattendedAsync() =>
        AnsweredByPolicyAsync("deny", PolicyAnswer.Deny, ItemOutcome.Cancelled);

    private async Task AnsweredByPolicyAsync(string answer, PolicyAnswer expected, ItemOutcome command)
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/permissions.json", MigrationsPolicy(answer)));

        var decision = await run.DecisionAsync();
        var turn = await run.TurnAsync();

        Assert.Equal(
            (expected, DecisionDelivery.Answered, "migrations", "dotnet ef database update"),
            (decision.Answer, decision.Delivery, Outcomes.Present(decision.Rule).Name, decision.Target));
        Assert.Contains(turn, update => update is ItemCompleted { Item.Value: "migrate" } completed && completed.Outcome == command);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var audit = run.Get<IPermissionAudit>();
        Assert.Equal([decision], audit.OfJob(run.Job));
        Assert.Equal(PolicyFileStatus.Applied, Outcomes.Present(audit.PolicyOf(decision.Session)).File);
    }

    [Fact]
    public async Task WithoutARepositoryPolicyThePermissionAwaitsAHumanAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");

        var decision = await run.DecisionAsync();

        Assert.Equal((PolicyAnswer.Ask, DecisionDelivery.LeftToHuman, true), (decision.Answer, decision.Delivery, decision.Rule.IsNone));
        Assert.Equal([decision], run.Get<IPermissionAudit>().OfJob(run.Job));
        Assert.Equal(PolicyFileStatus.Absent, Outcomes.Present(run.Get<IPermissionAudit>().PolicyOf(decision.Session)).File);
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(
            decision.Session,
            new PermissionDecision(decision.Item, PermissionAnswer.Allow),
            Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    private const string TestsPolicy = """{ "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test*", "answer": "allow" } ] }""";

    private const string CalculatorChecks = """
        {
          "checks": [
            { "name": "git", "command": "git", "arguments": ["--version"] },
            { "name": "calculator", "command": "git", "arguments": ["grep", "--quiet", "--fixed-strings", "add(2, 2) = 4", "--", "calculator.txt"], "timeoutSeconds": 60 }
          ]
        }
        """;

    private static string MigrationsPolicy(string answer) =>
        $$"""{ "rules": [ { "name": "migrations", "kind": "command", "target": "dotnet ef *", "answer": "{{answer}}" } ] }""";

    private static string Canvas(IReadOnlyList<CanvasSnapshot> snapshots, string item, string mediaType)
    {
        var canvas = snapshots.Where(snapshot => snapshot.Canvas.Item == new ItemId(item)).ToList();

        Assert.All(canvas, snapshot => Assert.Equal(mediaType, snapshot.MediaType));
        Assert.All(canvas.SkipLast(1), snapshot => Assert.Equal(CanvasStatus.Streaming, snapshot.Status));
        Assert.Equal(CanvasStatus.Completed, canvas[^1].Status);
        Assert.All(canvas.Zip(canvas.Skip(1)), pair => Assert.StartsWith(pair.First.Content, pair.Second.Content, StringComparison.Ordinal));

        return canvas[^1].Content;
    }
}
