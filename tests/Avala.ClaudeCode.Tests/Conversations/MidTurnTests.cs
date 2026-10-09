using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class MidTurnTests
{
    private static readonly UserTurn Steering = new("Keep the alias") { MidTurn = true };

    [Fact]
    public void AMessageMidTurnIsWrittenToClaudeCodeAndJoinsTheRunningTurn()
    {
        var talk = new Talk().Begin().Receive(Cli.Init());
        talk.Drain();

        var joined = talk.Conversation.Begin(Steering).Match(begun => begun, _ => default);
        talk.Take(joined.Reaction);

        Assert.Equal(talk.Turn, joined.Turn);
        Assert.Equal([new MessageQueued(talk.Session, talk.Turn, "Keep the alias")], talk.Events);
        Assert.Equal(("user", "Keep the alias"), ((string?)talk.Sent[0]["type"], (string?)talk.Sent[0]["message"]!["content"]));
    }

    [Fact]
    public void TheResultOfTheFirstMessageKeepsTheTurnOpenUntilClaudeCodeAnswersTheQueuedOne()
    {
        var talk = new Talk().Begin().Receive(Cli.Init());
        talk.Take(talk.Conversation.Begin(Steering).Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Cli.Result(0.010m));
        var carried = talk.Drain();
        talk.Receive(Cli.Result(0.025m));

        Assert.DoesNotContain(carried, agentEvent => agentEvent is TurnCompleted);
        Assert.Equal(0.010m, carried.OfType<UsageReported>().Single().Cost.Match(cost => cost.Amount, () => 0m));
        Assert.Equal(0.015m, talk.Events.OfType<UsageReported>().Single().Cost.Match(cost => cost.Amount, () => 0m));
        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Finished), talk.Events[^1]);
    }

    [Fact]
    public void AnInterruptionEndsTheTurnEvenWithAMessageQueued()
    {
        var talk = new Talk().Begin().Receive(Cli.Init());
        talk.Take(talk.Conversation.Begin(Steering).Match(begun => begun.Reaction, _ => Reaction.None));
        talk.Take(talk.Conversation.Interrupt().Match(begun => begun.Reaction, _ => Reaction.None));

        talk.Receive(Cli.Result(0.010m, "error_during_execution", isError: true));

        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Interrupted), talk.Events[^1]);
    }

    [Fact]
    public void AMessageMidTurnOutsideATurnIsRefused() =>
        Assert.Equal(AgentError.NoTurnInProgress, new Talk().Conversation.Begin(Steering).Match(_ => default, error => error));
}
