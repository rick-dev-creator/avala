using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class MidTurnNoticeTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Allows = "Avala now allows ";

    private const string Noticed = $"Noted: {Allows}";

    private const string GoOn = "Go on.";

    private const string Stopped = "Stopped the background agent once Avala allowed TaskStop.";

    private const string Steering = "Keep the calculator's old name as an alias.";

    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(89);

    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllowingADenialWhileTheJobRunsTellsTheAgentInItsRunningTurnAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", Autonomous)], pace: Pace);
        var pacer = new Pacer(run);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("tools"))));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        Assert.Equal(JobStatus.AwaitingReview, await pacer.UntilAsync(run.SettledAsync()));
        await workbench.ShowsAsync(() => Allowable(conversation).Count == 1);
        var activity = run.Watch<AgentActivity>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, GoOn, Cancellation));
        _ = await pacer.UntilAsync(activity.UntilAsync(update => update.Event is TurnStarted));
        await workbench.ShowsAsync(() => Running(conversation));

        await run.Ui.RunAsync(() => Allowable(conversation)[0].ExecuteAsync("AllowCommand"));
        Assert.Equal(JobStatus.AwaitingReview, await pacer.UntilAsync(run.SettledAsync()));

        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "TurnEndViewModel") == 2);
        var (prompts, messages) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Text"),
            OpenWorkbench.Texts(conversation, "MessageViewModel", "Text")));
        Assert.Equal(2, prompts.Count);
        Assert.Single(messages, text => text.StartsWith(Noticed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoIdenticalAllowNoticesInARowReachTheAgentOnceAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("repeated-denial");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Stop the background agent that surveys the greetings."),
            claude.Data,
            [(".avala/permissions.json", Autonomous)]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Running(conversation) && Allowable(conversation).Count == 2);

        await run.Ui.RunAsync(() => Allowable(conversation)[0].ExecuteAsync("AllowCommand"));
        await run.Ui.RunAsync(() => Allowable(conversation) is [var left, ..] ? left.ExecuteAsync("AllowCommand") : Task.CompletedTask);
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Stopped));

        var told = await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "InterjectionViewModel", "Text").Concat(OpenWorkbench.Texts(conversation, "PromptViewModel", "Text")).ToList());
        var heard = told.Sum(text => text.Split(Allows).Length - 1);
        Assert.True(heard == 1, $"The agent was told {heard} times: [{string.Join(" | ", told)}]");
    }

    [Fact]
    public async Task MessagesSentDuringTheTurnThatRunsOutOfItsWindowReachTheSessionThatContinuesTheJobAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoAccounts,
            [
                (".avala/checks.json", ReviewTests.CalculatorChecks),
                (".avala/permissions.json", ReviewTests.TestsPolicy),
                (".avala/jobs.json", """{ "limits": { "onLimit": "handoff-same-harness", "connections": ["simulator-one", "simulator-two"] } }"""),
            ],
            pace: Pace);
        var pacer = new Pacer(run);
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("account-exhausted")) { Connection = new ConnectionName("simulator-one") }));
        var first = (await pacer.UntilAsync(activity.UntilAsync(update => update.Event is TurnStarted))).Event.Session;

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SteerAsync(job, Steering, Cancellation));
        var continued = await pacer.UntilAsync(activity.CollectUntilAsync(update => update.Event is TurnCompleted && update.Event.Session != first));

        var told = continued.Where(update => update.Event.Session != first).Select(update => update.Event switch
        {
            ItemProgressed progressed => progressed.Text,
            MessageQueued queued => queued.Text,
            _ => string.Empty,
        }).Where(text => text.Length > 0).ToList();
        Assert.True(
            told.Any(text => text.Contains(Steering, StringComparison.Ordinal)),
            $"The session that continues the job was never told the message sent during the turn that ran out: [{string.Join(" | ", told)}]");
    }

    private static bool Running(Bound conversation) => conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Running;

    private static List<Bound> Allowable(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "DeniedCardViewModel" && entry["CanAllow"].Value<bool>())];
}
