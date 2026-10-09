using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class PermissionTests
{
    private const string Command = """{ "command": "ls", "description": "List" }""";

    private static readonly ItemId Item = new("t1");

    [Fact]
    public void AnAllowedCommandIsAnsweredWithItsInputAndReportsItsOutput()
    {
        var talk = Asked();

        talk.Take(talk.Conversation.Respond(new PermissionDecision(Item, PermissionAnswer.Allow)));
        var decision = talk.Decision("r1");
        talk.Receive(Cli.ToolResult("t1", "hello.txt"));

        Assert.Equal("allow", (string?)decision["behavior"]);
        Assert.Equal("ls", (string?)decision["updatedInput"]!["command"]);
        Assert.Equal<IAgentEvent>(
            [
                new PermissionResolved(talk.Session, talk.Turn, Item, PermissionAnswer.Allow),
                new ItemProgressed(talk.Session, talk.Turn, Item, "hello.txt"),
                new ItemCompleted(talk.Session, talk.Turn, Item, ItemOutcome.Succeeded),
            ],
            talk.Events.SkipWhile(agentEvent => agentEvent is not PermissionResolved));
    }

    [Fact]
    public void ADenialCarriesItsMessageToClaudeAndCancelsTheItemWithoutItsOutput()
    {
        var talk = Asked();

        talk.Take(talk.Conversation.Respond(new PermissionDecision(Item, PermissionAnswer.Deny) { Message = "Run echo instead." }));
        var decision = talk.Decision("r1");
        talk.Receive(Cli.ToolResult("t1", "Run echo instead.", isError: true));

        Assert.Equal("deny", (string?)decision["behavior"]);
        Assert.Equal("Run echo instead.", (string?)decision["message"]);
        Assert.Equal(
            new ItemCompleted(talk.Session, talk.Turn, Item, ItemOutcome.Cancelled),
            talk.Events[^1]);
        Assert.DoesNotContain(talk.Events, agentEvent => agentEvent is ItemProgressed);
    }

    [Fact]
    public void AnAnswerForAnItemThatIsNotAskingIsRefused()
    {
        var talk = Asked();

        Assert.Equal(
            AgentError.NoPendingPermission,
            talk.Conversation.Respond(new PermissionDecision(new ItemId("other"), PermissionAnswer.Allow)).Match(_ => default, error => error));
    }

    [Fact]
    public void ASecondPromptWaitsUntilTheFirstIsAnswered()
    {
        var talk = Asked().Receive(Cli.ToolUse("t2", "Bash", Command), Cli.Prompt("r2", "Bash", Command, "t2"));

        Assert.Equal([Item], talk.Events.OfType<PermissionRequested>().Select(requested => requested.Item));

        talk.Take(talk.Conversation.Respond(new PermissionDecision(Item, PermissionAnswer.Allow)));

        Assert.Equal([Item, new ItemId("t2")], talk.Events.OfType<PermissionRequested>().Select(requested => requested.Item));
    }

    [Theory]
    [InlineData("Bash", "{}", true)]
    [InlineData("Write", "{}", true)]
    [InlineData("Edit", "{}", true)]
    [InlineData("WebFetch", "{}", true)]
    [InlineData("RemoteTrigger", "{}", true)]
    [InlineData("mcp__github__create_issue", "{}", true)]
    [InlineData("Read", "{}", false)]
    [InlineData("Read", """{ "file_path": "/work/src/a.cs" }""", false)]
    [InlineData("Read", """{ "file_path": "src/a.cs" }""", false)]
    [InlineData("Read", """{ "file_path": "/etc/passwd" }""", true)]
    [InlineData("Read", """{ "file_path": "../secrets.txt" }""", true)]
    [InlineData("Read", """{ "file_path": "src/\u0000a.cs" }""", true)]
    [InlineData("Grep", """{ "pattern": "x" }""", false)]
    [InlineData("Grep", """{ "pattern": "x", "path": "/home" }""", true)]
    [InlineData("Glob", """{ "pattern": "*", "path": "/work-other" }""", true)]
    [InlineData("TodoWrite", "{}", false)]
    [InlineData("Agent", "{}", false)]
    [InlineData("mcp__avala__canvas", "{}", false)]
    public void ThePreToolUseHookSendsEveryActingToolAndEveryReadOutsideTheWorkingDirectoryToThePermissionPrompt(string tool, string input, bool asks)
    {
        var talk = new Talk().Begin().Receive(Cli.Hook("h1", tool, Cli.OnHost(input)));

        Assert.Equal(asks ? "ask" : null, (string?)talk.HookAnswer("h1")["hookSpecificOutput"]?["permissionDecision"]);
    }

    [Theory]
    [InlineData("/work/src/a.cs")]
    [InlineData(@"\work\src\a.cs")]
    [InlineData("C:work/src/a.cs")]
    public void AReadOfAPathRootedOnNoParticularDriveIsSentToThePermissionPrompt(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has paths that are rooted without being fully qualified.");

        var talk = new Talk().Begin().Receive(Cli.Hook("h1", "Read", new JsonObject { ["file_path"] = path }.ToJsonString()));

        Assert.Equal("ask", (string?)talk.HookAnswer("h1")["hookSpecificOutput"]?["permissionDecision"]);
    }

    [Theory]
    [InlineData(PermissionMode.AllowEdits, "Write", """{ "file_path": "/work/a.txt", "content": "a" }""", false)]
    [InlineData(PermissionMode.AllowEdits, "Bash", Command, true)]
    [InlineData(PermissionMode.AllowAll, "Bash", Command, false)]
    [InlineData(PermissionMode.AskEveryTime, "Write", """{ "file_path": "/work/a.txt", "content": "a" }""", true)]
    public void ThePermissionModeDecidesWhatIsAskedAndWhatIsAllowedAtOnce(PermissionMode mode, string tool, string input, bool asks)
    {
        var onHost = Cli.OnHost(input);
        var talk = new Talk(mode).Begin().Receive(Cli.ToolUse("t1", tool, onHost), Cli.Prompt("r1", tool, onHost, "t1"));

        Assert.Equal(asks, talk.Events.OfType<PermissionRequested>().Any());
        Assert.Equal(asks ? null : "allow", (string?)(asks ? null : talk.Decision("r1")["behavior"]));
    }

    [Fact]
    public void ThePermissionToolItselfIsNeverGranted()
    {
        var talk = new Talk().Begin().Receive(Cli.Prompt("r1", "mcp__avala__permission_prompt", "{}", "t1"));

        Assert.Equal("deny", (string?)talk.Decision("r1")["behavior"]);
        Assert.Empty(talk.Events.OfType<PermissionRequested>());
    }

    [Fact]
    public void APromptWithoutAToolUseSeenBeforeStartsItsItemFirst()
    {
        var talk = new Talk().Begin().Receive(Cli.Prompt("r1", "Bash", Command, "t1"));

        Assert.Equal(["ItemStarted", "PermissionRequested"], talk.Events.Skip(1).Select(agentEvent => agentEvent.GetType().Name));
    }

    private static Talk Asked() =>
        new Talk().Begin().Receive(Cli.ToolUse("t1", "Bash", Command), Cli.Hook("h1", "Bash", Command), Cli.Prompt("r1", "Bash", Command, "t1"));
}
