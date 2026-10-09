using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class StreamTests
{
    [Fact]
    public void AStreamedTextBlockIsAMessageThatProgressesWithEveryDeltaAndCompletes()
    {
        var talk = new Talk().Begin().Receive(Cli.Streamed("m1", 0, "text", "Hello ", "there"));
        var item = new ItemId("m1:0");

        Assert.Equal<IAgentEvent>(
            [
                new TurnStarted(talk.Session, talk.Turn),
                new ItemStarted(talk.Session, talk.Turn, item, ItemKind.Message, "Message"),
                new ItemProgressed(talk.Session, talk.Turn, item, "Hello "),
                new ItemProgressed(talk.Session, talk.Turn, item, "there"),
                new ItemCompleted(talk.Session, talk.Turn, item, ItemOutcome.Succeeded),
            ],
            talk.Events);
        Assert.Equal("user", (string?)Assert.Single(talk.Sent)["type"]);
    }

    [Fact]
    public void AStreamedThinkingBlockIsReasoningAndTheWholeAssistantMessageIsNotRepeated()
    {
        var talk = new Talk().Begin().Receive([.. Cli.Streamed("m1", 0, "thinking", "Weighing it"), Cli.Said("m1", "thinking", "Weighing it")]);

        Assert.Equal(
            [ItemKind.Reasoning],
            talk.Events.OfType<ItemStarted>().Select(started => started.Kind));
    }

    [Fact]
    public void AnAssistantMessageThatWasNotStreamedIsReportedWhole()
    {
        var talk = new Talk().Begin().Receive(Cli.Said("m2", "text", "Done."));

        Assert.Equal(
            ["ItemStarted", "ItemProgressed", "ItemCompleted"],
            talk.Events.Skip(1).Select(agentEvent => agentEvent.GetType().Name));
    }

    [Theory]
    [InlineData("Write", """{ "file_path": "/work/hello.txt", "content": "hello" }""", ItemKind.FileEdit, "Write hello.txt", "/work/hello.txt")]
    [InlineData("Edit", """{ "file_path": "src/a.cs", "old_string": "a", "new_string": "b" }""", ItemKind.FileEdit, "Edit src/a.cs", "/work/src/a.cs")]
    [InlineData("Bash", """{ "command": "dotnet test\nmore", "description": "Run the tests" }""", ItemKind.Command, "Run dotnet test", "dotnet test\nmore")]
    [InlineData("Grep", """{ "pattern": "TODO" }""", ItemKind.Search, "Search TODO", "TODO")]
    [InlineData("Glob", """{ "pattern": "**/*.cs" }""", ItemKind.Search, "Search **/*.cs", "**/*.cs")]
    [InlineData("WebFetch", """{ "url": "https://example.com", "prompt": "Read" }""", ItemKind.Web, "Fetch https://example.com", "https://example.com")]
    [InlineData("WebSearch", """{ "query": "avalonia" }""", ItemKind.Web, "Search the web for avalonia", "avalonia")]
    [InlineData("mcp__github__list", """{ }""", ItemKind.Mcp, "mcp__github__list", "mcp__github__list")]
    [InlineData("Agent", """{ "description": "Explore", "prompt": "Look", "subagent_type": "Explore" }""", ItemKind.Subagent, "Subagent: Explore", "Explore")]
    [InlineData("Read", """{ "file_path": "/work/a.txt" }""", ItemKind.Other, "Read a.txt", "/work/a.txt")]
    public void EveryToolUseIsAnItemOfItsKindWithATitleAndTheTargetItsPermissionNames(string tool, string input, ItemKind kind, string title, string target)
    {
        var onHost = Cli.OnHost(input);
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", tool, onHost)).Receive(Cli.Prompt("r1", tool, onHost, "t1"));

        Assert.Equal(new ItemStarted(talk.Session, talk.Turn, new ItemId("t1"), kind, title), talk.Events.OfType<ItemStarted>().Single());
        Assert.Equal(
            new PermissionRequested(talk.Session, talk.Turn, new ItemId("t1"), title, kind, HostPaths.Rooted(target)),
            talk.Events.OfType<PermissionRequested>().Single());
    }

    [Fact]
    public void ATodoListUpdatesThePlan()
    {
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", "TodoWrite", """
            { "todos": [ { "content": "Write", "status": "completed", "activeForm": "Writing" },
                         { "content": "Test", "status": "in_progress", "activeForm": "Testing" },
                         { "content": "Ship", "status": "pending", "activeForm": "Shipping" } ] }
            """));

        Assert.Equal(
            [new PlanStep("Write", PlanStepStatus.Done), new PlanStep("Test", PlanStepStatus.InProgress), new PlanStep("Ship", PlanStepStatus.Pending)],
            Assert.Single(talk.Events.OfType<PlanUpdated>()).Steps);
        Assert.Empty(talk.Events.OfType<ItemStarted>());
    }

    [Fact]
    public void TheSessionIdIsIssuedAsAResumeTokenOncePerTurn()
    {
        var talk = new Talk().Begin().Receive(Cli.Init(), Cli.Init());

        Assert.Equal(
            new ResumeTokenIssued(talk.Session, talk.Turn, new ConversationMark(Guid.Parse(Cli.Session), 0m).Token),
            Assert.Single(talk.Events.OfType<ResumeTokenIssued>()));
    }

    [Fact]
    public void TheResultReportsTheTurnsUsageWithTheCostItAddedAndFinishesTheTurn()
    {
        var talk = new Talk().Begin().Receive(Cli.Init(), Cli.Result(0.0018m));
        talk.Drain();
        talk.Begin().Receive(Cli.Result(0.0026m));

        Assert.Equal(
            new UsageReported(talk.Session, talk.Turn, new TokenUsage(4, 200, 30611, 6910, 12), new Cost(0.0008m, "USD")),
            Assert.Single(talk.Events.OfType<UsageReported>()));
        Assert.Equal(new ConversationMark(Guid.Parse(Cli.Session), 0.0026m).Token, talk.Events.OfType<ResumeTokenIssued>().Last().Token);
        Assert.Equal(new TurnCompleted(talk.Session, talk.Turn, TurnOutcome.Finished), talk.Events[^1]);
        Assert.True(talk.Conversation.Live.IsNone);
    }

    [Fact]
    public void AResumedConversationCountsOnlyTheCostAddedSinceItsToken()
    {
        var talk = new Talk(resumed: new ConversationMark(Guid.Parse(Cli.Session), 0.0036m)).Begin().Receive(Cli.Result(0.0039m));

        Assert.Equal(new Cost(0.0003m, "USD"), Assert.Single(talk.Events.OfType<UsageReported>()).Cost);
    }

    [Theory]
    [InlineData("error_max_turns", true)]
    [InlineData("error_during_execution", true)]
    [InlineData("success", true)]
    public void AResultThatIsAnErrorFailsTheTurn(string subtype, bool isError)
    {
        var talk = new Talk().Begin().Receive(Cli.Result(0.001m, subtype, isError));

        Assert.Equal(TurnOutcome.Failed, Assert.IsType<TurnCompleted>(talk.Events[^1]).Outcome);
    }

    [Fact]
    public void ARateLimitEventReportsEveryWindowWithItsUseAndReset()
    {
        var talk = new Talk().Begin().Receive(Cli.RateLimit());

        Assert.Equal(
            [
                new UsageLimit("5h", 0, DateTimeOffset.FromUnixTimeSeconds(1791580200)),
                new UsageLimit("7d", 0.99, DateTimeOffset.FromUnixTimeSeconds(1791691200)),
            ],
            talk.Events.OfType<LimitReported>().Select(reported => reported.Limit));
    }

    [Fact]
    public void ATurnCannotBeginWhileAnotherIsLive()
    {
        var talk = new Talk().Begin();

        Assert.Equal(AgentError.TurnInProgress, talk.Conversation.Begin(new UserTurn("again")).Match(_ => default, error => error));
    }

    [Fact]
    public void ATurnEndingWithAnItemStillOpenClosesItBeforeTheTurnEnds()
    {
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", "Agent", """{ "description": "Explore" }"""), Cli.Result(0.001m));

        Assert.Equal(
            new ItemCompleted(talk.Session, talk.Turn, new ItemId("t1"), ItemOutcome.Failed),
            talk.Events.OfType<ItemCompleted>().Single());
    }
}
