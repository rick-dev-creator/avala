using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class SubagentLifecycleTests(PublishedPlugins plugins)
{
    private const string Survey = "Survey the greeting";

    private const string Build = "Check the build";

    private const string Background = "Survey the greeting and check the build in the background.";

    private static readonly string[] Described = ["Text", "Title", "Summary", "Reason"];

    private static readonly TimeSpan PromptCache = TimeSpan.FromHours(1);

    private static readonly (string File, string Content)[] Login =
    [
        ("connections.json", """{ "default": "work", "connections": [ { "name": "work", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/work/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABackgroundSubagentIsShownWorkingUntilItsNotificationAndCompletedAfterACompletedNotificationAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Background), claude.Data, []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var (workbench, conversation, pane) = await PaneAsync(run);
        var launched = await StatesAsync(run, pane);

        await TellAsync(run, workbench, conversation, "How are they doing?", 3);
        var notified = await StatesAsync(run, pane);

        Assert.Equal(("running", "done"), (launched.GetValueOrDefault(Survey), notified.GetValueOrDefault(Survey)));
    }

    [Fact]
    public async Task ABackgroundSubagentWhoseNotificationSaysStoppedIsShownStoppedAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Background), claude.Data, []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var (workbench, conversation, pane) = await PaneAsync(run);
        await TellAsync(run, workbench, conversation, "How are they doing?", 3);

        await TellAsync(run, workbench, conversation, "And the build check?", 5);
        var stopped = await StatesAsync(run, pane);

        Assert.Equal("stopped", stopped.GetValueOrDefault(Build));
    }

    [Fact]
    public async Task ConcurrentBackgroundSubagentsAreTrackedIndependentlySoOneFinishingLeavesTheOtherWorkingAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Background), claude.Data, []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var (workbench, conversation, pane) = await PaneAsync(run);
        var launched = await StatesAsync(run, pane);

        await TellAsync(run, workbench, conversation, "How are they doing?", 3);
        var oneFinished = await StatesAsync(run, pane);

        Assert.Equal(
            [("running", "running"), ("done", "running")],
            new[] { launched, oneFinished }.Select(states => (states.GetValueOrDefault(Survey), states.GetValueOrDefault(Build))).ToList());
    }

    [Fact]
    public async Task ASubagentStillWorkingWhenItsParentsSessionEndsBecomesLostAndBothTheConversationAndTheOrchestratorAreToldAsync()
    {
        var lost = await LostAsync();

        Assert.Equal(
            ("lost", true, true),
            (lost.State, lost.Conversation.Any(Mentioned), lost.Told.Any(Mentioned)));
    }

    [Fact]
    public async Task AResumedOrchestratorAsksAvalaForItsSubagentsAndGetsTheirRealStatusAsync()
    {
        var lost = await LostAsync();

        Assert.Contains(Survey, lost.Answer, StringComparison.Ordinal);
        Assert.Contains("lost", lost.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubagentWhoseToolCallAsksAPersonIsShownAwaitingPermissionAsItselfAndTheOrchestratorCanLearnItAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-asks");
        await using var tapped = new TemporaryFolder();
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, Background),
            claude.Tapping(tapped.Path),
            [(".avala/permissions.json", """{ "autonomy": "supervised" }""")]);
        var asked = await run.DecisionAsync();
        var (_, _, pane) = await PaneAsync(run);
        var waiting = await StatesAsync(run, pane);

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        _ = await run.StopAndReadRecordingsAsync();
        var answer = Answer(await TapAsync(tapped.Path), "b4call02-0000-4000-8000-000000000001");

        Assert.Equal(
            ("npm install", "waiting", "running", true),
            (asked.Target, waiting.GetValueOrDefault(Build), waiting.GetValueOrDefault(Survey), answer.Contains(Build, StringComparison.Ordinal) && answer.Contains("permission", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ADelegatedChildJobAndAClaudeCodeSubagentReportTheSameLifecycleStateAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            Login,
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["work"], "maxChildren": 1 } }"""),
            ]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-agents] Ship the release")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var pane = await OpenPaneAsync(run, workbench);
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["Settled"].Items.Count == 1, () => "no child in the pane");

        var (child, subagent) = await run.Ui.ReadAsync(() =>
        {
            var node = pane["Settled"].Items[0];

            return (
                node.Has("StateText") ? node["StateText"].Text : $"no lifecycle state, only the job status {node["Status"].Text}",
                pane["Root"]["Subagents"].Items.Single(agent => agent["Name"].Text == "Plan the split")["StateText"].Text);
        });

        Assert.Equal(subagent, child);
    }

    private static bool Mentioned(string text) => text.Contains(Survey, StringComparison.Ordinal);

    private async Task<(string State, IReadOnlyList<string> Conversation, IReadOnlyList<string> Told, string Answer)> LostAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-lost");
        await using var tapped = new TemporaryFolder();
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Survey the greeting in the background."),
            claude.Tapping(tapped.Path),
            []);
        var stopped = run.Watch<SessionStopped>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();
        var session = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation)).Sessions[^1].Session;
        var (workbench, conversation, pane) = await PaneAsync(run);
        var before = await run.Ui.ReadAsync(() => Shown(conversation));

        run.Clock.Advance(PromptCache);
        _ = await stopped.UntilAsync(found => found.Session == session);
        await run.DeliveredAsync();
        await workbench.ShowsAsync(() => true);
        var state = (await StatesAsync(run, pane)).GetValueOrDefault(Survey, "missing");
        var shown = (await run.Ui.ReadAsync(() => Shown(conversation))).Except(before).ToList();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, "How is the survey going?", Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        _ = await run.StopAndReadRecordingsAsync();
        var tap = await TapAsync(tapped.Path);
        var told = tap.SkipWhile(line => line["resume"] is null).Where(line => line["in"]?["type"]?.GetValue<string>() == "user").Select(line => line["in"]!["message"]!["content"]!.ToJsonString()).ToList();

        return (state, shown, told, Answer(tap, "b3call01-0000-4000-8000-000000000001"));
    }

    private static List<string> Shown(Bound conversation) =>
        [.. conversation["Entries"].Items.SelectMany(entry => Described.Where(entry.Has).Select(property => entry[property].Text))];

    private static string Answer(IReadOnlyList<JsonNode> tap, string request) =>
        tap.Select(line => line["in"]?["response"])
            .Where(response => response?["request_id"]?.GetValue<string>() == request)
            .Select(response => response?["response"]?["mcp_response"]?["result"]?.ToJsonString() ?? response?["response"]?["mcp_response"]?.ToJsonString() ?? string.Empty)
            .FirstOrDefault() ?? "Avala never answered the call.";

    private static async Task<IReadOnlyList<JsonNode>> TapAsync(string folder)
    {
        var lines = new List<JsonNode>();

        foreach (var file in Directory.GetFiles(folder, "*.jsonl").Order(StringComparer.Ordinal))
        {
            lines.AddRange((await File.ReadAllLinesAsync(file, Cancellation)).Where(line => line.Length > 0).Select(line => JsonNode.Parse(line)!));
        }

        return lines;
    }

    private static async Task<(OpenWorkbench Workbench, Bound Conversation, Bound Pane)> PaneAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);

        return (workbench, conversation, await OpenPaneAsync(run, workbench));
    }

    private static async Task<Bound> OpenPaneAsync(SimulatedRun run, OpenWorkbench workbench)
    {
        var toggle = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "DelegationToggleViewModel");
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "DelegationPaneViewModel");
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "toggle hidden");

        if (!await run.Ui.ReadAsync(() => pane["IsOpen"].Value<bool>()))
        {
            await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        }

        await run.Ui.PresentedAsync(pane.Presentation, () => pane["IsOpen"].Value<bool>() && pane.Has("Root"), () => "pane empty");

        return pane;
    }

    private static Task<Dictionary<string, string>> StatesAsync(SimulatedRun run, Bound pane) =>
        run.Ui.ReadAsync(() => pane["Root"]["Subagents"].Items.ToDictionary(agent => agent["Name"].Text, agent => agent["StateText"].Text));

    private static async Task TellAsync(SimulatedRun run, OpenWorkbench workbench, Bound conversation, string message, int turnsAfter)
    {
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, message, Cancellation));
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "TurnEndViewModel") >= turnsAfter);
        await run.DeliveredAsync();
    }
}
