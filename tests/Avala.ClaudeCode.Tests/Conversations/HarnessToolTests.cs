using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class HarnessToolTests
{
    private const string Svg = """{ "title": "Circle", "mediaType": "image/svg+xml", "content": "<svg/>" }""";

    private static readonly HarnessTool Canvas = new("canvas", "Draw a canvas.", """{ "type": "object" }""", ToolSurface.Canvas);

    private static readonly HarnessTool FollowUp = new(
        "propose_follow_up",
        "Propose a task.",
        """{ "type": "object", "properties": { "instruction": { "type": "string" } } }""",
        ToolSurface.Executed);

    [Fact]
    public void TheServerListsThePermissionToolAndEveryOfferedToolWithItsSchema()
    {
        var talk = new Talk(tools: [Canvas, FollowUp]).Receive(
            Cli.Mcp("r0", """{ "method": "initialize", "params": { "protocolVersion": "2025-11-25" }, "jsonrpc": "2.0", "id": 0 }"""),
            Cli.Mcp("r1", """{ "method": "tools/list", "jsonrpc": "2.0", "id": 1 }"""));

        Assert.Equal("2025-11-25", (string?)talk.McpResult("r0")["protocolVersion"]);
        Assert.Equal(
            ["permission_prompt", "canvas", "propose_follow_up"],
            talk.McpResult("r1")["tools"]!.AsArray().Select(tool => (string?)tool!["name"]));
        Assert.Equal("string", (string?)talk.McpResult("r1")["tools"]![2]!["inputSchema"]!["properties"]!["instruction"]!["type"]);
    }

    [Fact]
    public void ACanvasCallIsDrawnWithoutAskingAndAnsweredAtOnce()
    {
        var talk = new Talk(tools: [Canvas]).Begin().Receive(
            Cli.ToolUse("c1", "mcp__avala__canvas", Svg),
            Cli.Prompt("r1", "mcp__avala__canvas", Svg, "c1"),
            Cli.McpCall("r2", "canvas", Svg, "c1"),
            Cli.ToolResult("c1", "The canvas is drawn."));
        var item = new ItemId("c1");

        Assert.Equal("allow", (string?)talk.Decision("r1")["behavior"]);
        Assert.False((bool)talk.McpResult("r2")["isError"]!);
        Assert.Equal<IAgentEvent>(
            [
                new CanvasStarted(talk.Session, talk.Turn, item, "Circle", "image/svg+xml"),
                new ItemProgressed(talk.Session, talk.Turn, item, "<svg/>"),
                new ItemCompleted(talk.Session, talk.Turn, item, ItemOutcome.Succeeded),
            ],
            talk.Events.Skip(1));
    }

    [Fact]
    public void ACanvasWhoseInputCannotBeReadFails()
    {
        var talk = new Talk(tools: [Canvas]).Begin().Receive(Cli.McpCall("r2", "canvas", """{ "title": "Empty" }""", "c1"));

        Assert.True((bool)talk.McpResult("r2")["isError"]!);
        Assert.Equal(ItemOutcome.Failed, talk.Events.OfType<ItemCompleted>().Single().Outcome);
    }

    [Fact]
    public void AnExecutedCallWaitsForTheHarnessResultAndHandsItToClaude()
    {
        var talk = new Talk(tools: [FollowUp]).Begin().Receive(
            Cli.ToolUse("f1", "mcp__avala__propose_follow_up", """{ "instruction": "Announce" }"""),
            Cli.McpCall("r2", "propose_follow_up", """{ "instruction": "Announce" }""", "f1"));
        var item = new ItemId("f1");
        var result = new ToolResult(item, "Proposed.");

        Assert.Equal(new ToolCalled(talk.Session, talk.Turn, item, "propose_follow_up", """{"instruction":"Announce"}"""), talk.Events[^1]);
        Assert.Equal(AgentError.NoPendingCall, talk.Conversation.Return(new ToolResult(new ItemId("other"), "x")).Match(_ => default, error => error));

        talk.Take(talk.Conversation.Return(result)).Receive(Cli.ToolResult("f1", "Proposed."));

        Assert.Equal("Proposed.", (string?)talk.McpResult("r2")["content"]![0]!["text"]);
        Assert.Equal<IAgentEvent>(
            [new ToolReturned(talk.Session, talk.Turn, item, result), new ItemCompleted(talk.Session, talk.Turn, item, ItemOutcome.Succeeded)],
            talk.Events.SkipWhile(agentEvent => agentEvent is not ToolReturned));
    }

    [Fact]
    public void ACallOfAToolTheSessionWasNotGivenIsAnError()
    {
        var talk = new Talk().Begin().Receive(Cli.McpCall("r2", "delegate", "{}", "d1"));

        Assert.True((bool)talk.McpResult("r2")["isError"]!);
        Assert.Single(talk.Events);
    }
}
