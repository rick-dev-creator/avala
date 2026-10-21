using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ClaudeCodePermissionTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly string[] Decided = ["ask", "deny"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Autonomies => ["supervised", "autonomous"];

    [Theory]
    [MemberData(nameof(Autonomies))]
    public async Task AClaudeCodeAgentReadsTheToolResultsOfItsOwnSessionWithoutAskingAHumanAsync(string autonomy)
    {
        using var claude = await TranscribedClaude.PrepareAsync("own-tool-results");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Find every greeting in the project and list them in GREETINGS.md."),
            claude.Data,
            autonomy == "autonomous" ? [(".avala/permissions.json", Autonomous)] : []);

        var decisions = await AllowedOrStoppedAsync(run, 1);

        Assert.Equal([], NotAllowed(decisions));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AClaudeCodeAgentReadsTheOutputFileOfItsOwnBackgroundTaskWithoutAskingAHumanAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("own-task-output");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "List the project's files in the background, then tell me what is there."),
            claude.Data,
            []);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal([], NotAllowed(run.Get<IPermissionAudit>().OfJob(run.Job)));
    }

    [Fact]
    public async Task AClaudeCodeAgentReadsMessagesAndStopsItsOwnBackgroundAgentsWithoutAskingAHumanAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("own-background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Survey the project with a background agent, steer it, then stop it."),
            claude.Data,
            []);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal([], NotAllowed(run.Get<IPermissionAudit>().OfJob(run.Job)));
    }

    [Fact]
    public async Task AvalaDecidesAClaudeCodeAgentLaunchedInAnIsolatedWorktreeBeforeItLaunchesAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("isolated-agent");
        await using var tapped = new TemporaryFolder();
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Try the greeting change in an isolated agent."),
            claude.Tapping(tapped.Path),
            []);

        _ = await run.SettledAsync();
        _ = await run.StopAndReadRecordingsAsync();
        var lines = (await File.ReadAllLinesAsync(Assert.Single(Directory.GetFiles(tapped.Path, "*.jsonl")), Cancellation)).Select(line => JsonNode.Parse(line)!).ToList();
        var gate = lines
            .Select(line => line["out"])
            .OfType<JsonObject>()
            .Single(line => (string?)line["request"]?["subtype"] == "hook_callback" && (string?)line["request"]?["input"]?["tool_name"] == "Agent");
        var answered = lines
            .Select(line => line["in"])
            .OfType<JsonObject>()
            .Single(line => (string?)line["response"]?["request_id"] == (string?)gate["request_id"]);

        Assert.Contains((string?)answered["response"]?["response"]?["hookSpecificOutput"]?["permissionDecision"], Decided);
    }

    [Fact]
    public async Task AClaudeCodeWriteIntoAnIsolatedWorktreeOfTheMainRepositoryGoesToAHumanAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("agent-worktree-write");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Try the greeting change in an isolated agent."),
            claude.Data,
            []);

        var write = (await run.DecisionsUntilAsync(decision => decision.Kind == ItemKind.FileEdit))[^1];

        Assert.Equal(
            (Path.Combine(run.Repository.Path, ".claude", "worktrees", "agent-a4f81c2d", "README.md"), PolicyAnswer.Ask, DecisionDelivery.LeftToHuman, "edits-outside-the-workspace-go-to-a-human"),
            (write.Target, write.Answer, write.Delivery, write.Rule.Match(rule => rule.Name, () => string.Empty)));
    }

    [Fact]
    public async Task AReadOfAnImageUnderTheSystemTemporaryFolderAsksAHumanAndAllowingItLetsTheClaudeCodeAgentRetryItAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("tmp-image");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Look at the screenshot at /tmp/avala-replayed-screenshot.png and describe it."),
            claude.Data,
            [(".avala/permissions.json", Autonomous)]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.AwaitingReview
            && OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value.ExecuteAsync("AllowCommand"));

        var (denied, retried) = await run.DecisionsAsync(2) is [var first, var second] ? (first, second) : default;
        Assert.Equal(("/tmp/avala-replayed-screenshot.png", PolicyAnswer.Deny), (denied.Target, denied.Answer));
        Assert.Equal(("/tmp/avala-replayed-screenshot.png", PolicyAnswer.Allow), (retried.Target, retried.Answer));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    private static Task<IReadOnlyList<PolicyDecision>> AllowedOrStoppedAsync(SimulatedRun run, int expected)
    {
        var allowed = 0;

        return run.DecisionsUntilAsync(decision => decision.Answer != PolicyAnswer.Allow || ++allowed == expected);
    }

    private static (ItemKind Kind, string Target, PolicyAnswer Answer, DecisionDelivery Delivery, string Rule)[] NotAllowed(IReadOnlyList<PolicyDecision> decisions) =>
        [.. decisions
            .Where(decision => decision.Answer != PolicyAnswer.Allow)
            .Select(decision => (decision.Kind, decision.Target, decision.Answer, decision.Delivery, decision.Rule.Match(rule => rule.Name, () => string.Empty)))];
}
