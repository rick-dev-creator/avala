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

    private static readonly string[] Described = ["Text", "Title", "Summary", "Reason", "Lost"];

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
        var (workbench, conversation, pane) = await PaneAsync(run);
        var launched = await StatesWhenAsync(run, workbench, conversation, pane, "Both sub-agents are working in the background.", (Survey, "working"), (Build, "working"));

        await SayAsync(run, conversation, "How are they doing?");
        var notified = await StatesWhenAsync(run, workbench, conversation, pane, "The survey is back: the greeting lives in src/Greeter.cs.", (Survey, "completed"));

        Assert.Equal(("working", "completed"), (launched.GetValueOrDefault(Survey), notified.GetValueOrDefault(Survey)));
    }

    [Fact]
    public async Task ABackgroundSubagentWhoseNotificationSaysStoppedIsShownStoppedAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Background), claude.Data, []);
        var (workbench, conversation, pane) = await PaneAsync(run);
        _ = await StatesWhenAsync(run, workbench, conversation, pane, "Both sub-agents are working in the background.");
        await SayAsync(run, conversation, "How are they doing?");
        _ = await StatesWhenAsync(run, workbench, conversation, pane, "The survey is back: the greeting lives in src/Greeter.cs.");

        await SayAsync(run, conversation, "And the build check?");
        var stopped = await StatesWhenAsync(run, workbench, conversation, pane, "The build check was stopped before it finished.", (Build, "stopped"));

        Assert.Equal("stopped", stopped.GetValueOrDefault(Build));
    }

    [Fact]
    public async Task ConcurrentBackgroundSubagentsAreTrackedIndependentlySoOneFinishingLeavesTheOtherWorkingAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-agents");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Background), claude.Data, []);
        var (workbench, conversation, pane) = await PaneAsync(run);
        var launched = await StatesWhenAsync(run, workbench, conversation, pane, "Both sub-agents are working in the background.", (Survey, "working"), (Build, "working"));

        await SayAsync(run, conversation, "How are they doing?");
        var oneFinished = await StatesWhenAsync(run, workbench, conversation, pane, "The survey is back: the greeting lives in src/Greeter.cs.", (Survey, "completed"));

        Assert.Equal(
            [("working", "working"), ("completed", "working")],
            new[] { launched, oneFinished }.Select(states => (states.GetValueOrDefault(Survey), states.GetValueOrDefault(Build))).ToList());
    }

    [Fact]
    public async Task ASubagentStillWorkingWhenAvalaStopsBecomesLostAndTheConversationSaysSoAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-lost");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, "Survey the greeting in the background."), claude.Data, []);
        _ = await run.RepliedAsync();
        await run.ResumableAsync();
        await run.DeliveredAsync();

        await run.RestartAsync();
        var (workbench, conversation, pane) = await PaneAsync(run);
        await workbench.ShowsAsync(() => Shown(conversation).Any(Mentioned) && SubagentNamed(pane, Survey).Exists);
        var (state, shown) = await run.Ui.ReadAsync(() => (SubagentNamed(pane, Survey).State, Shown(conversation)));

        Assert.Equal(("lost", true), (state, shown.Any(text => text.Contains("work is lost", StringComparison.Ordinal) && Mentioned(text))));
    }

    [Fact]
    public async Task AResumedOrchestratorAsksAvalaForItsSubagentsAndGetsTheirRealStatusAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-lost");
        await using var tapped = new TemporaryFolder();
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, "Survey the greeting in the background."), claude.Tapping(tapped.Path), []);
        _ = await run.RepliedAsync();
        await run.ResumableAsync();
        await run.DeliveredAsync();
        await run.RestartAsync();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        _ = await run.StopAndReadRecordingsAsync();

        var answer = Answer(await TapAsync(tapped.Path), "b3call01-0000-4000-8000-000000000001");

        Assert.Contains(Survey, answer, StringComparison.Ordinal);
        Assert.Contains("lost", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABackgroundSubagentsToolCallStillWaitingForAPersonWhenItsOrchestratorRepliesEndsOnlyWithItsOwnResultAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-asks-carried");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, Background),
            claude.Data,
            [(".avala/permissions.json", """{ "autonomy": "supervised" }""")]);
        var asked = await run.DecisionAsync();
        var replied = await run.RepliedAsync();

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        var rest = await run.TurnAsync();

        var completions = replied.Concat(rest).OfType<ItemCompleted>().Where(completed => completed.Item == asked.Item).ToList();
        Assert.Equal(
            (0, ItemOutcome.Succeeded),
            (replied.OfType<ItemCompleted>().Count(completed => completed.Item == asked.Item), Assert.Single(completions).Outcome));
    }

    [Fact]
    public async Task ASubagentWhoseToolCallAsksAPersonIsShownAwaitingPermissionAsItselfWhileTheOtherKeepsRunningAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-asks");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, Background),
            claude.Data,
            [(".avala/permissions.json", """{ "autonomy": "supervised" }""")]);
        var asked = await run.DecisionAsync();
        var (workbench, _, pane) = await PaneAsync(run);
        await workbench.ShowsAsync(() => SubagentNamed(pane, Build).State == "waiting");
        var waiting = await StatesAsync(run, pane);

        Assert.Equal(("npm install", "waiting", "working"), (asked.Target, waiting.GetValueOrDefault(Build), waiting.GetValueOrDefault(Survey)));
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
        await run.Ui.PresentedAsync(pane.Presentation, () => Branches(pane, "DelegationNodeViewModel").Count() == 1, () => "no child in the pane");

        var (child, subagent) = await run.Ui.ReadAsync(() =>
        {
            var node = Branches(pane, "DelegationNodeViewModel").Single();

            return (
                node.Has("StateText") ? node["StateText"].Text : $"no lifecycle state, only the job status {node["Status"].Text}",
                Branches(pane, "SubagentViewModel").Single(agent => agent["Name"].Text == "Plan the split")["StateText"].Text);
        });

        Assert.Equal(subagent, child);
    }

    private static bool Mentioned(string text) => text.Contains(Survey, StringComparison.Ordinal);

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

        await run.Ui.PresentedAsync(pane.Presentation, () => pane["IsOpen"].Value<bool>() && pane["HasTree"].Value<bool>(), () => "pane empty");

        return pane;
    }

    private static Task<Dictionary<string, string>> StatesAsync(SimulatedRun run, Bound pane) =>
        run.Ui.ReadAsync(() => Branches(pane, "SubagentViewModel").ToDictionary(agent => agent["Name"].Text, agent => agent["StateText"].Text));

    private static IEnumerable<Bound> Branches(Bound pane, string kind) =>
        pane["HasTree"].Value<bool>() ? pane["Tree"]["Branches"].Items.Where(branch => branch.Kind == kind) : [];

    private static (string State, bool Exists) SubagentNamed(Bound pane, string name) =>
        Branches(pane, "SubagentViewModel").FirstOrDefault(agent => agent["Name"].Text == name) is { } found
            ? (found["StateText"].Text, true)
            : (string.Empty, false);

    private static async Task<Dictionary<string, string>> StatesWhenAsync(SimulatedRun run, OpenWorkbench workbench, Bound conversation, Bound pane, string said, params (string Name, string State)[] settled)
    {
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(said));
        await run.DeliveredAsync();
        await workbench.ShowsAsync(() => settled.All(agent => SubagentNamed(pane, agent.Name).State == agent.State));

        return await StatesAsync(run, pane);
    }

    private static Task SayAsync(SimulatedRun run, Bound conversation, string message) =>
        run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", message);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
}
