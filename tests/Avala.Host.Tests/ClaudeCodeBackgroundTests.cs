using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ClaudeCodeBackgroundTests(PublishedPlugins plugins)
{
    private const string Instruction = "[simulate: reply] Have a background agent look over billing.";

    private const string Launched = "Launching a background agent for that.";

    private const string Background = "Survey the billing module";

    private static readonly TimeSpan PromptCache = TimeSpan.FromHours(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABackgroundSubAgentsReportAndTheAgentsAnswerToItBelongToTheTurnThatLaunchedItAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-woken");
        await using var run = await StartAsync(claude);
        var turn = await run.TurnAsync();
        await EvaluatedAsync(run);
        var conversation = await ShownAsync(run);

        var messages = await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text"));

        Assert.Equal(
            (1, TurnOutcome.Finished, ItemOutcome.Succeeded),
            (turn.OfType<TurnStarted>().Count(), Assert.IsType<TurnCompleted>(turn[^1]).Outcome, turn.OfType<ItemCompleted>().Single(completed => turn.OfType<ItemBackgrounded>().Any(backgrounded => backgrounded.Item == completed.Item)).Outcome));
        Assert.Contains("The billing survey came back: three entry points, no blockers.", messages);
    }

    [Fact]
    public async Task AnAgentWithBackgroundTasksPendingIsNotRestedOnceItsPromptCacheExpiresAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background");
        await using var run = await StartAsync(claude);
        var session = (await run.OpenedAsync()).Session;
        _ = await run.RepliedAsync();
        await EvaluatedAsync(run);

        run.Clock.Advance(PromptCache + PromptCache);
        await EvaluatedAsync(run);

        Assert.True(run.Get<IAgents>().IsOpen(session), "Avala stopped the agent while its background sub-agent was still working.");
    }

    [Fact]
    public async Task AnAgentInATurnItWokeOnItsOwnIsNotRestedOnceItsPromptCacheExpiresAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("woken-unannounced");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Instruction), claude.Data, [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var session = (await run.OpenedAsync()).Session;
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        _ = await run.DecisionAsync();
        await EvaluatedAsync(run);

        run.Clock.Advance(PromptCache + PromptCache);
        await EvaluatedAsync(run);

        Assert.True(run.Get<IAgents>().IsOpen(session), "Avala stopped the agent while a turn it woke on its own was live.");
    }

    [Fact]
    public async Task AnAgentKeptForATurnItWokeOnItsOwnEndsOnceThatTurnEndsAndItsPromptCacheExpiresAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("woken-idle");
        await using var run = await StartAsync(claude);
        var session = (await run.OpenedAsync()).Session;
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var asked = await run.DecisionAsync();
        await EvaluatedAsync(run);
        run.Clock.Advance(PromptCache + PromptCache);
        await EvaluatedAsync(run);
        Assert.True(run.Get<IAgents>().IsOpen(session));
        var ended = run.Watch<TurnFinished>();
        var stopped = run.Watch<SessionStopped>();

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        _ = await ended.UntilAsync(finished => finished.Session == session);
        await EvaluatedAsync(run);
        var before = await StateAsync(run, session);
        run.Clock.Advance(PromptCache + PromptCache);

        try
        {
            _ = await stopped.UntilAsync(found => found.Session == session);
        }
        catch (TimeoutException timeout)
        {
            throw new TimeoutException($"{timeout.Message}; before the clock moved {before}; after {await StateAsync(run, session)}", timeout);
        }
    }

    private static async Task<string> StateAsync(SimulatedRun run, SessionId session)
    {
        await run.DeliveredAsync();
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));
        var agents = run.Get<IAgents>();

        return $"job {history.Summary.Status}, hold {history.Summary.Hold}, attempts {history.Attempts.Count}, sessions {history.Sessions.Count}, open {agents.IsOpen(session)}, working {agents.IsWorking(session)}";
    }

    [Fact]
    public async Task AJobWhoseAgentStillHasBackgroundWorkAfterItsResultShowsAsWorkingAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background");
        await using var run = await StartAsync(claude);
        _ = await run.RepliedAsync();
        await EvaluatedAsync(run);

        var status = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation)).Summary.Status;

        Assert.Equal(JobStatus.Running, status);
    }

    [Fact]
    public async Task StoppingASessionWithBackgroundWorkPendingTellsTheConversationWhatWasStoppedAndWhyAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background");
        await using var run = await StartAsync(claude);
        var session = (await run.OpenedAsync()).Session;
        _ = await run.RepliedAsync();
        await EvaluatedAsync(run);
        var conversation = await ShownAsync(run);
        var stopped = run.Watch<SessionStopped>();

        Outcomes.Succeeds(await run.Get<IAgents>().StopAsync(session, Cancellation));
        _ = await stopped.UntilAsync(found => found.Session == session);
        await run.DeliveredAsync();

        var (told, said) = await run.Ui.ReadAsync(() => (Telling(conversation), string.Join(" | ", Said(conversation))));
        Assert.True(told.Count > 0, $"Nothing outside the Agent call itself told the person that \"{Background}\" was stopped and why; the conversation said: {said}");
    }

    [Fact]
    public async Task AWokenTurnThatNeverGetsItsResultIsClosedWhenItsSessionEndsAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-woken-open");
        await using var run = await StartAsync(claude);
        var session = (await run.OpenedAsync()).Session;
        await run.ProgressedAsync("The billing survey came back: ");

        Outcomes.Succeeds(await run.Get<IAgents>().StopAsync(session, Cancellation));
        await run.DeliveredAsync();
        _ = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = new ConnectionName(TranscribedClaude.Simulator) }));
        var next = await run.TurnAsync();

        Assert.True(
            next.OfType<TurnCompleted>().Any(completed => completed.Session == session),
            $"The woken turn was left open: the next turn that ended was {next[^1]}");
    }

    [Fact]
    public async Task AfterAvalaRestartsTheBackgroundAgentsThatDiedWithThePreviousSessionAreReportedInTheConversationAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-resumed");
        await using var run = await StartAsync(claude);
        _ = await run.RepliedAsync();
        await EvaluatedAsync(run);

        await run.RestartAsync();
        _ = await run.TurnAsync();
        await EvaluatedAsync(run);
        var conversation = await ShownAsync(run, "Going on with the billing module.");

        var (told, said) = await run.Ui.ReadAsync(() => (Telling(conversation), string.Join(" | ", Said(conversation))));
        Assert.True(told.Count > 0, $"Nothing told the person that \"{Background}\" died with the previous session; the conversation said: {said}");
    }

    [Fact]
    public async Task HandingOffAJobWhoseSessionHasBackgroundWorkPendingTellsThePersonWhatIsLostAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("background-handoff");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, Instruction),
            claude.Data,
            [(".avala/jobs.json", """{ "limits": { "onLimit": "handoff-any-harness", "connections": ["claude", "sim"] } }""")]);
        _ = await run.TurnAsync();
        var handedOff = await run.TurnAsync();
        await EvaluatedAsync(run);
        var conversation = await ShownAsync(run);

        var (told, said) = await run.Ui.ReadAsync(() => (Telling(conversation), string.Join(" | ", Said(conversation))));
        var attempts = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation)).Attempts;
        Assert.Equal(AttemptOrigin.Handoff, attempts[^1].Origin);
        Assert.NotEqual((await run.OpenedAsync()).Session, handedOff[^1].Session);
        Assert.True(told.Count > 0, $"The handoff did not tell the person that \"{Background}\" is lost with the old session; the conversation said: {said}");
    }

    private Task<SimulatedRun> StartAsync(TranscribedClaude claude) =>
        SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Instruction), claude.Data, []);

    private static async Task EvaluatedAsync(SimulatedRun run)
    {
        for (var round = 0; round < 3; round++)
        {
            await run.DeliveredAsync();
            _ = await run.Get<IJobCheckpoints>().OfAsync(run.Job, Cancellation);
        }

        await run.DeliveredAsync();
    }

    private static async Task<Bound> ShownAsync(SimulatedRun run, string message = Launched)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(message));

        return conversation;
    }

    private static List<string> Telling(Bound conversation) =>
        [.. conversation["Entries"].Items
            .Where(entry => entry.Kind != "ToolViewModel" && entry.Has("SearchText"))
            .Select(entry => entry["SearchText"].Text)
            .Where(text => text.Contains(Background, StringComparison.Ordinal))];

    private static List<string> Said(Bound conversation) =>
        [.. conversation["Entries"].Items.Select(entry => entry.Has("SearchText") ? $"{entry.Kind}: {entry["SearchText"].Text}" : entry.Kind)];
}
