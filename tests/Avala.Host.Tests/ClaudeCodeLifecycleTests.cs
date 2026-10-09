using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Supervision.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ClaudeCodeLifecycleTests(PublishedPlugins plugins)
{
    private const string Interrupted = "claude-code-interrupt";

    private const string Resumed = "claude-code-resume";

    private const string LongCall = "claude-code-delegate";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ChildWait = TimeSpan.FromHours(2);

    private static readonly (string Path, string Content) Delegation = (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AClaudeCodeTurnThatFallsSilentIsInterruptedMidTurnAndItsJobHeldAsStalledAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("interrupt");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Rewrite the whole parser module."),
            [.. claude.Data, Silence(Window)],
            []);

        await run.SilentForAsync(Window);

        await AssertStalledAsync(run);
        await AssertCommittedAsync(Interrupted, Assert.Single(await run.StopAndReadRecordingsAsync()));
    }

    [Fact]
    public async Task TheRecordedClaudeCodeInterruptionReplaysThroughTheSimulatorToTheSameHoldAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            RecordingFixtures.Replay(Interrupted),
            [($"recordings/{Interrupted}.json", await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(Interrupted), Cancellation)), Silence(Window)],
            []);

        await run.SilentForAsync(Window);

        await AssertStalledAsync(run);
    }

    [Fact]
    public async Task AClaudeCodeJobRecoveredAfterARestartResumesItsConversationWithItsTokenAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("resume");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, "Write the release notes."), claude.Data, []);
        await run.ResumableAsync();

        await run.RestartAsync();

        _ = await run.OpenedAsync();
        var turn = await run.TurnAsync();
        Assert.Equal("1d3b5c77-6e8f-4093-8b14-ae6f8a0b2c07/0.0035", turn.OfType<ResumeTokenIssued>().Last().Token.Value);
        Assert.Equal(new Cost(0.0035m, "USD"), Outcomes.Present(Assert.Single(turn.OfType<UsageReported>()).Cost));
        Assert.Contains(turn, agentEvent => agentEvent is ItemProgressed { Text: "the release notes are gathered." });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var recordings = await run.StopAndReadRecordingsAsync();
        Assert.Equal([false, true], recordings.Select(recording => JsonNode.Parse(recording)!["options"]!["resumed"]!.GetValue<bool>()).Order());
        await AssertCommittedAsync(Resumed, recordings.Single(recording => JsonNode.Parse(recording)!["options"]!["resumed"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task AHarnessCallThatWaitsHoursForItsChildIsAnsweredWhenTheChildFinishesAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("delegate");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a sub-agent migrate the database."),
            [.. claude.Data, Silence(TimeSpan.FromMinutes(10))],
            [Delegation]);

        await AnswerTheChildAfterHoursAsync(run);

        var recording = (await run.StopAndReadRecordingsAsync()).Single(text => JsonNode.Parse(text)!["provider"]!["id"]!.GetValue<string>() == "claude-code");
        var entries = JsonNode.Parse(recording)!["entries"]!.AsArray();
        var called = entries.First(entry => entry!["event"]?["type"]?.GetValue<string>() == "toolCalled")!["at"]!.GetValue<long>();
        var returned = entries.First(entry => entry!["return"] is not null)!["at"]!.GetValue<long>();
        Assert.True(returned - called >= ChildWait.TotalMilliseconds, $"The call was answered {returned - called} ms after it was made");
        await AssertCommittedAsync(LongCall, recording);
    }

    [Fact]
    public async Task TheRecordedLongCallReplaysThroughTheSimulatorAndWaitsForItsChildAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            RecordingFixtures.Replay(LongCall),
            [
                ("connections.json", """{ "default": "sim", "connections": [ { "name": "sim", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
                ("connections/sim/.login", string.Empty),
                ($"recordings/{LongCall}.json", await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(LongCall), Cancellation)),
                Silence(TimeSpan.FromMinutes(10)),
            ],
            [Delegation]);

        await AnswerTheChildAfterHoursAsync(run);
    }

    private static async Task AnswerTheChildAfterHoursAsync(SimulatedRun run)
    {
        var asked = await run.DecisionAsync();
        run.Clock.Advance(ChildWait);

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        Assert.Empty(run.Get<ISupervision>().OfJob(run.Job));
    }

    private static async Task AssertStalledAsync(SimulatedRun run)
    {
        var turn = await run.TurnAsync();

        Assert.Equal(TurnOutcome.Interrupted, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var hold = await run.SupervisorInterventionAsync();
        Assert.Equal((HoldReason.Stalled, SessionHalt.Interrupted), (hold.Hold.Reason, hold.Hold.Halt));
    }

    private static async Task AssertCommittedAsync(string name, string recording)
    {
        if (RecordingFixtures.Refreshing)
        {
            await File.WriteAllTextAsync(RecordingFixtures.RecordingOf(name), recording, Cancellation);
        }

        Assert.Equal(
            TranscribedClaude.Comparable(await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(name), Cancellation)),
            TranscribedClaude.Comparable(recording));
    }

    private static (string File, string Content) Silence(TimeSpan window) =>
        ("supervision.json", $$"""{ "silenceSeconds": {{window.TotalSeconds.ToString(CultureInfo.InvariantCulture)}} }""");
}
