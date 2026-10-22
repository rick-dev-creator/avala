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
    public void ASubagentsReplyThatItsNotificationRepeatsAsItsSummaryIsReportedOnce()
    {
        var talk = new Talk().Begin();
        talk.Receive(
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Cli.Parse("""{ "type": "system", "subtype": "task_started", "task_id": "k1", "tool_use_id": "a1", "description": "Survey", "is_backgrounded": true }"""),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Parse("""{ "type": "assistant", "message": { "id": "s1", "role": "assistant", "content": [{ "type": "text", "text": "started" }] }, "parent_tool_use_id": "a1" }"""),
            Cli.Parse("""{ "type": "system", "subtype": "task_notification", "task_id": "k1", "tool_use_id": "a1", "status": "completed", "summary": "started" }"""));

        Assert.Equal(["started"], talk.Events.OfType<ItemProgressed>().Where(progressed => progressed.Item == new ItemId("a1")).Select(progressed => progressed.Text));
    }

    [Fact]
    public void ABackgroundSubagentClaudeCodeReportsFailedAfterTheReplyEndsFailedWithItsReason()
    {
        var talk = new Talk().Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"));
        talk.Drain();

        talk.Receive(Cli.Parse("""{ "type": "system", "subtype": "task_notification", "task_id": "k1", "tool_use_id": "a1", "status": "failed", "summary": "API Error: overloaded" }"""));

        Assert.Equal(
            (ItemOutcome.Failed, "API Error: overloaded"),
            (talk.Events.OfType<ItemCompleted>().Single(completed => completed.Item == new ItemId("a1")).Outcome, string.Concat(talk.Events.OfType<ItemProgressed>().Where(progressed => progressed.Item == new ItemId("a1")).Select(progressed => progressed.Text))));
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

    [Fact]
    public void ASubagentClaudeCodeStartsAgainWhileNoTurnIsLiveIsShownAsANewRunThatCarriesItsSteps()
    {
        var talk = new Talk(PermissionMode.AllowAll).Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"),
            Completed(),
            Cli.Said("m2", "text", "The survey came back."),
            Cli.Result(0.009m));
        talk.Drain();

        talk.Receive(Started(), Cli.Nested(Cli.ToolUse("n1", "Read", """{ "file_path": "/work/billing.md" }"""), "a1"), Cli.Prompt("r1", "Read", """{ "file_path": "/work/billing.md" }""", "n1"));

        Assert.Contains(talk.Events.OfType<ItemStarted>(), started => started.Item == new ItemId("a1:run2"));
    }

    [Fact]
    public void AToolCallOfASubagentClaudeCodeStartedAgainBelongsToItsNewRun()
    {
        var talk = new Talk(PermissionMode.AllowAll).Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"),
            Completed());
        talk.Drain();

        talk.Receive(Started(), Cli.Nested(Cli.ToolUse("n1", "Read", """{ "file_path": "/work/billing.md" }"""), "a1"));

        Assert.Equal("a1:run2", talk.Events.OfType<ItemStarted>().Single(started => started.Item == new ItemId("n1")).Parent.Match(parent => parent.Value, () => string.Empty));
    }

    [Fact]
    public void ASubagentsToolCallStillOpenWhenTheReplyEndsIsCarriedWhileTheSubagentWorksInTheBackground()
    {
        var talk = new Talk(PermissionMode.AllowAll).Begin();

        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Nested(Cli.ToolUse("n1", "Read", """{ "file_path": "/work/billing.md" }"""), "a1"),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"));

        Assert.Contains(talk.Events.OfType<ItemStarted>(), started => started.Item == new ItemId("n1"));
        Assert.DoesNotContain(talk.Events.OfType<ItemCompleted>(), completed => completed.Item == new ItemId("n1"));
    }

    [Fact]
    public void ALateNotificationNamingOnlyTheTaskOfASubagentThatFinishedReportsNothing()
    {
        var talk = new Talk(PermissionMode.AllowAll).Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(),
            Cli.ToolResult("a1", "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"),
            Completed(),
            Cli.Said("m2", "text", "The survey came back."),
            Cli.Result(0.009m));
        talk.Drain();

        talk.Receive(Cli.Parse("""{ "type": "system", "subtype": "task_notification", "task_id": "k1", "status": "completed", "summary": "Survey" }"""));

        Assert.Empty(talk.Events);
    }

    private static System.Text.Json.Nodes.JsonNode Started() =>
        Cli.Parse("""{ "type": "system", "subtype": "task_started", "task_id": "k1", "tool_use_id": "a1", "description": "Survey", "subagent_type": "general-purpose", "is_backgrounded": true }""");

    private static System.Text.Json.Nodes.JsonNode Completed() =>
        Cli.Parse("""{ "type": "system", "subtype": "task_notification", "task_id": "k1", "tool_use_id": "a1", "status": "completed", "summary": "Survey" }""");

    private static Talk Replied(Talk talk)
    {
        talk.Begin().Receive(Cli.Init(), Cli.Said("m1", "text", "Started."), Cli.Result(0.002m));
        talk.Drain();

        return talk;
    }
}
