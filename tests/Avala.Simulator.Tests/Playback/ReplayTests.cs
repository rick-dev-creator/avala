using System.Collections.Concurrent;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Playback;
using Avala.Simulator.Recordings;
using Avala.Simulator.Scenarios;
using Avala.Simulator.Tests.Recordings;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class ReplayTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReplayPlaysTheRecordedEventsInOrderAsItsOwnSessionAndTurnAndWritesTheRecordedFilesAsync()
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.Edit));
        var turn = await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);

        var events = await stage.ReadTurnAllowingEveryRequestAsync(Cancellation);

        Assert.All(events, agentEvent => Assert.Equal((stage.Session.Id, turn), (agentEvent.Session, agentEvent.Turn)));
        Assert.Equal(
            ["TurnStarted", "ResumeTokenIssued", "ItemStarted", "PermissionRequested", "PermissionResolved", "ItemProgressed", "ItemCompleted", "UsageReported", "TurnCompleted"],
            events.Select(agentEvent => agentEvent.GetType().Name));
        Assert.Equal(Path.Combine(stage.WorkingDirectory, "GREETING.md").Replace('\\', '/'), Assert.Single(events.OfType<PermissionRequested>()).Target.Replace('\\', '/'));
        Assert.NotEqual("provider-token", Assert.Single(events.OfType<ResumeTokenIssued>()).Token.Value);
        Assert.Equal(new Cost(0.25m, "USD"), Outcomes.Present(Assert.Single(events.OfType<UsageReported>()).Cost));
        Assert.Equal("# Hello\n", await File.ReadAllTextAsync(Path.Combine(stage.WorkingDirectory, "docs", "GREETING.md"), Cancellation));
    }

    [Fact]
    public async Task AnAnswerThatDiffersFromTheRecordedOneIsReportedAsADivergenceThatFailsTheTurnAndClosesTheSessionAsync()
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.Edit));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);
        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);

        Outcomes.Succeeds(await stage.Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Deny) { Message = "Not now" }, Cancellation));
        var rest = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal(
            "Replay diverged: the recording answered the permission for edit with Allow, but the harness answered Deny \"Not now\".",
            Assert.Single(rest.OfType<ItemProgressed>()).Text);
        Assert.Equal(TurnOutcome.Failed, Assert.IsType<TurnCompleted>(rest[^1]).Outcome);
        Assert.False(File.Exists(Path.Combine(stage.WorkingDirectory, "docs", "GREETING.md")));
        Assert.Empty(await Stage.ReadUntilAsync<TurnCompleted>(stage.Session, Cancellation));
        Assert.Equal(AgentError.SessionClosed, Outcomes.FailsWith(await stage.Session.SendAsync(new UserTurn("Go on"), Cancellation)));
    }

    [Theory]
    [InlineData("PostgreSQL", "")]
    [InlineData("SQLite", "Replay diverged: the recording answered the form question with database: PostgreSQL, but the harness answered database: SQLite.")]
    public async Task AFormAnswerIsHonoredOnlyWhenItIsTheRecordedOneAsync(string chosen, string divergence)
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.Question));
        await stage.SendAsync("[replay: recorded] Store the orders", Cancellation);
        var asked = Assert.IsType<FormRequested>((await stage.ReadUntilAsync<FormRequested>(Cancellation))[^1]);

        Outcomes.Succeeds(await stage.Session.AnswerAsync(new FormAnswer(asked.Item, [new FieldAnswer("database") { Chosen = [chosen] }]), Cancellation));
        var rest = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal(divergence.Length == 0 ? TurnOutcome.Finished : TurnOutcome.Failed, Assert.IsType<TurnCompleted>(rest[^1]).Outcome);
        Assert.Equal(divergence.Length == 0 ? [] : [divergence], rest.OfType<ItemProgressed>().Select(progressed => progressed.Text));
    }

    [Fact]
    public async Task ATurnTheRecordingDoesNotHoldIsADivergenceAsync()
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.TurnStarted, Recorded.Finished));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);
        await stage.ReadTurnAsync(Cancellation);

        await stage.SendAsync("And again", Cancellation);

        Assert.Equal(
            "Replay diverged: the recording holds 1 turn, but the harness started another.",
            Assert.Single((await stage.ReadTurnAsync(Cancellation)).OfType<ItemProgressed>()).Text);
    }

    [Fact]
    public async Task ASessionInAnotherPermissionModeThanTheRecordingIsADivergenceAsync()
    {
        await using var stage = await StageAsync(Recorded.SessionIn("allowAll", Recorded.Edit));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal(["TurnStarted", "ItemStarted", "ItemProgressed", "ItemCompleted", "TurnCompleted"], events.Select(agentEvent => agentEvent.GetType().Name));
        Assert.Equal("Replay diverged: the recording was made in AllowAll, but this session runs in AskEveryTime.", events.OfType<ItemProgressed>().Single().Text);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("../recorded")]
    public async Task ARecordingThatCannotBeFoundIsReportedAsADivergenceAsync(string name)
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.Edit));
        await stage.SendAsync($"[replay: {name}] Greet the team", Cancellation);

        Assert.Equal(
            $"Replay diverged: the recording {name} cannot be replayed: NotFound.",
            Assert.Single((await stage.ReadTurnAsync(Cancellation)).OfType<ItemProgressed>()).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnInterruptionIsHonoredWhenTheRecordingWasInterruptedThereAndADivergenceOtherwiseAsync(bool recorded)
    {
        string[] interrupted = [Recorded.TurnStarted, """{ "at": 20, "interrupt": {} }""", Recorded.Finished];
        string[] waiting = [Recorded.TurnStarted];
        await using var stage = await StageAsync(Recorded.Session(recorded ? interrupted : waiting));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);
        await stage.ReadUntilAsync<TurnStarted>(Cancellation);

        Outcomes.Succeeds(await stage.Session.InterruptAsync(Cancellation));
        var rest = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal(recorded ? TurnOutcome.Interrupted : TurnOutcome.Failed, Assert.IsType<TurnCompleted>(rest[^1]).Outcome);
        Assert.Equal(
            recorded ? [] : ["Replay diverged: the harness interrupted the turn, which the recording never did."],
            rest.OfType<ItemProgressed>().Select(progressed => progressed.Text));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARecordedEndOfTheStreamEndsTheReplayedStreamTheSameWayAsync(bool crashed)
    {
        await using var stage = await StageAsync(Recorded.Session(Recorded.TurnStarted, Recorded.Finished, $$"""{ "at": 95, "end": { "crashed": {{(crashed ? "true" : "false")}} } }"""));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);
        await stage.ReadTurnAsync(Cancellation);

        var ending = Stage.ReadUntilAsync<TurnCompleted>(stage.Session, Cancellation);

        if (crashed)
        {
            Assert.Equal("The recorded agent's event stream failed.", (await Assert.ThrowsAsync<InvalidOperationException>(() => ending)).Message);
        }
        else
        {
            Assert.Empty(await ending);
        }
    }

    [Fact]
    public async Task AResumedReplayContinuesWithTheNextRecordedTurnAsync()
    {
        string[] twoTurns =
        [
            Recorded.TurnStarted,
            Recorded.Finished,
            """{ "at": 100, "event": { "type": "turnStarted", "turn": 2 } }""",
            """{ "at": 101, "event": { "type": "itemStarted", "turn": 2, "item": "second", "kind": "message", "title": "Reply" } }""",
            """{ "at": 102, "event": { "type": "itemCompleted", "turn": 2, "item": "second", "outcome": "succeeded" } }""",
            """{ "at": 103, "event": { "type": "turnCompleted", "turn": 2, "outcome": "finished" } }""",
        ];
        await using var stage = await StageAsync(Recorded.Session([Recorded.TurnStarted, """{ "at": 11, "event": { "type": "resumeTokenIssued", "turn": 1, "token": "t" } }""", .. twoTurns[1..]]));
        await stage.SendAsync("[replay: recorded] Greet the team", Cancellation);
        var token = Assert.Single((await stage.ReadTurnAsync(Cancellation)).OfType<ResumeTokenIssued>()).Token;

        await using var resumed = Outcomes.Succeeds(await new SimulatedProvider(stage.Craft).StartAsync(
            new SessionOptions(stage.WorkingDirectory, PermissionMode.AskEveryTime) { Resume = token },
            Cancellation));
        Outcomes.Succeeds(await resumed.SendAsync(new UserTurn("Continue"), Cancellation));

        Assert.Contains(await Stage.ReadUntilAsync<TurnCompleted>(resumed, Cancellation), agentEvent => agentEvent is ItemStarted { Item.Value: "second" });
    }

    [Theory]
    [InlineData(true, new[] { 10, 80 })]
    [InlineData(false, new int[0])]
    public async Task AsRecordedTheReplayWaitsTheRecordedGapsAndCompressedItDoesNotAsync(bool asRecorded, int[] gaps)
    {
        var clock = new GapClock();
        using var data = new TemporaryFolder();
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(data.Path, RecordingFolder.FolderName));
        await File.WriteAllTextAsync(Path.Combine(data.Path, RecordingFolder.FolderName, "recorded.json"), Recorded.Session(Recorded.TurnStarted, Recorded.Finished), Cancellation);
        var craft = Stage.Crafted(new AvalaPaths(data.Path)) with { Pacing = new Pacing(clock, TimeSpan.Zero) };
        await using var session = new SimulatedSession(new SessionOptions(folder.Path, PermissionMode.AskEveryTime), craft, Option<Conversation>.None);

        Outcomes.Succeeds(await session.SendAsync(new UserTurn(asRecorded ? "[replay as recorded: recorded] Go" : "[replay: recorded] Go"), Cancellation));

        Assert.Equal(["TurnStarted", "TurnCompleted"], (await Stage.ReadUntilAsync<TurnCompleted>(session, Cancellation)).Select(agentEvent => agentEvent.GetType().Name));
        Assert.Equal(gaps.Select(gap => TimeSpan.FromMilliseconds(gap)), clock.Waited);
    }

    private sealed class GapClock : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> waited = new();

        public IReadOnlyList<TimeSpan> Waited => [.. waited];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            waited.Enqueue(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new Fired();
        }

        private sealed class Fired : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async Task<Stage> StageAsync(string recording)
    {
        var stage = new Stage();
        Directory.CreateDirectory(stage.Recordings);
        await File.WriteAllTextAsync(Path.Combine(stage.Recordings, "recorded.json"), recording, Cancellation);

        return stage;
    }
}
