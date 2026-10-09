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
    public void NothingToInterruptOutsideATurn()
    {
        var talk = new Talk();

        Assert.Equal(AgentError.NoTurnInProgress, talk.Conversation.Interrupt().Match(_ => default, error => error));
    }
}
