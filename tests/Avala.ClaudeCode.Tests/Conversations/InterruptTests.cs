using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class InterruptTests
{
    private const string Command = """{ "command": "sleep 20", "description": "Wait" }""";

    [Fact]
    public void AnInterruptionAsksClaudeToStopAndRefusesWhatItWaitsFor()
    {
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", "Bash", Command), Cli.Prompt("r1", "Bash", Command, "t1"));

        var interrupted = talk.Conversation.Interrupt().Match(begun => begun, _ => default);
        talk.Take(interrupted.Reaction);

        Assert.Equal(talk.Turn, interrupted.Turn);
        Assert.Equal("deny", (string?)talk.Decision("r1")["behavior"]);
        Assert.Equal("interrupt", (string?)talk.Sent[^1]["request"]!["subtype"]);
    }

    [Fact]
    public void TheResultAfterAnInterruptionEndsTheTurnInterruptedWithItsItemsCancelled()
    {
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", "Bash", Command), Cli.Prompt("r1", "Bash", Command, "t1"));
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Cli.ToolResult("t1", "The user doesn't want to proceed.", isError: true), Cli.Result(0.002m, "error_during_execution", isError: true));

        Assert.Equal(new ItemCompleted(talk.Session, talk.Turn, new ItemId("t1"), ItemOutcome.Cancelled), talk.Events.OfType<ItemCompleted>().Single());
        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Interrupted), talk.Events[^1]);
    }

    [Fact]
    public void AnInterruptionWhenClaudeCodeOwesNoResultEndsTheTurnAtOnce()
    {
        var talk = new Talk().Begin();
        talk.Receive(Cli.Lifecycle(talk.Command, "started"), Cli.Init());
        talk.Take(talk.Conversation.Begin(new UserTurn("Keep the alias") { MidTurn = true }).Match(begun => begun.Reaction, _ => Reaction.None));
        talk.Receive(Cli.Lifecycle(talk.Command, "queued"), Cli.Result(0.010m), Cli.Lifecycle(talk.Command, "cancelled"));
        talk.Drain();

        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        Assert.Empty(talk.Sent);
        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Interrupted), talk.Events[^1]);
    }

    [Fact]
    public void InterruptingATurnTheAgentWokeOnItsOwnAsksClaudeCodeToStopAndWaitsForItsResult()
    {
        var talk = new Talk().Begin();
        talk.Receive(Cli.Lifecycle(talk.Command, "queued"), Cli.Lifecycle(talk.Command, "started"), Cli.Init(), Cli.Said("m1", "text", "Started."), Cli.Result(0.002m), Cli.Lifecycle(talk.Command, "completed"));
        talk.Receive(Cli.Init(), Cli.Said("m2", "text", "The survey came back, going on."));
        var woken = Assert.IsType<TurnStarted>(talk.Events.Last(agentEvent => agentEvent is TurnStarted));
        talk.Drain();

        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        Assert.True(woken.Woken);
        Assert.Equal(
            ("interrupt", false),
            ((string?)talk.Sent.LastOrDefault()?["request"]?["subtype"], talk.Events.OfType<TurnCompleted>().Any()));
    }

    [Fact]
    public void ASecondInterruptBeforeClaudeCodeAcknowledgesTheFirstStillEndsATurnKeptLiveOnlyByBackgroundWorkOnTheAcknowledgement()
    {
        var talk = Carried(new Talk());
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));
        talk.Receive(Notification("stopped"));
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Acknowledgement(1), Acknowledgement(2));

        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Interrupted), talk.Events.OfType<TurnCompleted>().SingleOrDefault());
    }

    [Fact]
    public void ABackgroundSubagentClaudeCodeReportsStoppedAfterAcknowledgingTheInterruptEndsCancelledInTheInterruptedTurn()
    {
        var talk = Carried(new Talk());
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Acknowledgement(1), Notification("stopped"));

        Assert.Equal(
            (new ItemCompleted(talk.Session, talk.Turn, new ItemId("a1"), ItemOutcome.Cancelled), 0),
            (talk.Events.OfType<ItemCompleted>().FirstOrDefault(completed => completed.Item == new ItemId("a1")), talk.Events.OfType<TurnStarted>().Count()));
    }

    [Fact]
    public void InterruptingATurnABackgroundSubagentsToolCallOpenedWhileTheAgentWasIdleEndsWhenClaudeCodeAcknowledges()
    {
        var talk = new Talk().Begin();
        talk.Receive(Cli.Lifecycle(talk.Command, "queued"), Cli.Lifecycle(talk.Command, "started"), Cli.Init(), Cli.Said("m1", "text", "Started."), Cli.Result(0.002m), Cli.Lifecycle(talk.Command, "completed"));
        talk.Receive(Cli.Prompt("r1", "Read", """{ "file_path": "/work/README.md" }""", "t1"));
        var woken = Assert.IsType<TurnStarted>(talk.Events.Last(agentEvent => agentEvent is TurnStarted));
        talk.Drain();
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Acknowledgement(1));

        Assert.Equal(new TurnCompleted(talk.Session, woken.Turn, TurnOutcome.Interrupted), talk.Events.OfType<TurnCompleted>().SingleOrDefault());
    }

    [Fact]
    public void WhatClaudeCodeStillStreamsOfAWokenTurnAfterAcknowledgingItsInterruptOpensNoNewTurn()
    {
        var talk = Woken(new Talk());
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));
        talk.Receive(Acknowledgement(1));
        talk.Drain();

        talk.Receive(
            Cli.Stopped(0),
            Cli.Parse("""{ "type": "user", "message": { "role": "user", "content": [{ "type": "text", "text": "[Request interrupted by user]" }] }, "parent_tool_use_id": null }"""),
            Cli.Result(0.004m, "error_during_execution", isError: true));

        Assert.Empty(talk.Events.OfType<TurnStarted>());
    }

    private static Talk Woken(Talk talk)
    {
        talk.Begin();
        talk.Receive(Cli.Lifecycle(talk.Command, "queued"), Cli.Lifecycle(talk.Command, "started"), Cli.Init(), Cli.Said("m1", "text", "Started."), Cli.Result(0.002m), Cli.Lifecycle(talk.Command, "completed"));
        talk.Receive(Cli.Init(), Cli.Said("m2", "text", "The survey came back, going on."));
        talk.Drain();

        return talk;
    }

    private static Talk Carried(Talk talk)
    {
        talk.Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Cli.Parse("""{ "type": "system", "subtype": "task_started", "task_id": "k1", "tool_use_id": "a1", "description": "Survey", "is_backgrounded": true }"""),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"));
        talk.Drain();

        return talk;
    }

    private static JsonNode Notification(string status) =>
        Cli.Parse($$"""{ "type": "system", "subtype": "task_notification", "task_id": "k1", "tool_use_id": "a1", "status": "{{status}}", "summary": "Survey" }""");

    private static JsonNode Acknowledgement(int interruption) =>
        Cli.Parse($$"""{ "type": "control_response", "response": { "subtype": "success", "request_id": "avala-interrupt-{{interruption}}", "response": { "still_queued": [] } } }""");

    [Fact]
    public void NothingToInterruptOutsideATurn()
    {
        var talk = new Talk();

        Assert.Equal(AgentError.NoTurnInProgress, talk.Conversation.Interrupt().Match(_ => default, error => error));
    }
}
