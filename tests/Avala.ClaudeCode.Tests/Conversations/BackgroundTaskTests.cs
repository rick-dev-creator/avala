using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class BackgroundTaskTests
{
    [Fact]
    public void ABackgroundSubAgentsToolCallAfterTheReplyEndedOpensATurnAndIsServed()
    {
        var talk = Replied(new Talk(PermissionMode.AllowAll));

        talk.Receive(Cli.Prompt("r1", "Read", """{ "file_path": "/work/README.md" }""", "t1"));

        Assert.Equal("allow", (string?)talk.Decision("r1")["behavior"]);
        Assert.True(Assert.IsType<TurnStarted>(Assert.Single(talk.Events)).Woken);
    }

    [Fact]
    public void TheTurnClaudeCodeOpensForANotificationShowsItsTextAndEndsWithItsResult()
    {
        var talk = Replied(new Talk());

        talk.Receive(Cli.Said("m2", "text", "The survey came back."), Cli.Result(0.004m));

        var started = Assert.IsType<TurnStarted>(talk.Events[0]);
        Assert.True(started.Woken);
        Assert.Equal("The survey came back.", talk.Events.OfType<ItemProgressed>().Single().Text);
        Assert.Equal(new TurnCompleted(talk.Session, started.Turn, TurnOutcome.Finished), talk.Events[^1]);
    }

    [Fact]
    public void ATurnAvalasMessageOpensIsNotOneTheAgentWokeOnItsOwn()
    {
        var talk = new Talk();

        talk.Begin();

        Assert.False(Assert.IsType<TurnStarted>(Assert.Single(talk.Events)).Woken);
    }

    [Fact]
    public void TheServersHandshakeWhileIdleOpensNoTurn()
    {
        var talk = new Talk().Receive(
            Cli.Mcp("r0", """{ "method": "initialize", "params": { "protocolVersion": "2025-11-25" }, "jsonrpc": "2.0", "id": 0 }"""),
            Cli.Mcp("r1", """{ "method": "tools/list", "jsonrpc": "2.0", "id": 1 }"""));

        Assert.Empty(talk.Events);
        Assert.Equal(Option<TurnId>.None, talk.Conversation.Live);
    }

    private static Talk Replied(Talk talk)
    {
        talk.Begin().Receive(Cli.Init(), Cli.Said("m1", "text", "Started."), Cli.Result(0.002m));
        talk.Drain();

        return talk;
    }
}
