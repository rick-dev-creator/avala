using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;
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
    [InlineData("Write", """{ "file_path": "/work/hello.txt", "content": "hello" }""", ItemKind.FileEdit, "Write hello.txt", "/work/hello.txt", "hello")]
    [InlineData("Edit", """{ "file_path": "src/a.cs", "old_string": "a", "new_string": "b" }""", ItemKind.FileEdit, "Edit src/a.cs", "/work/src/a.cs", "- a\n+ b")]
    [InlineData(
        "MultiEdit",
        """{ "file_path": "/work/b.txt", "edits": [ { "old_string": "x", "new_string": "y" }, { "old_string": "p\nq", "new_string": "r" } ] }""",
        ItemKind.FileEdit,
        "Edit b.txt",
        "/work/b.txt",
        "- x\n+ y\n\n- p\n- q\n+ r")]
    [InlineData("NotebookEdit", """{ "notebook_path": "/work/n.ipynb", "new_source": "print(1)" }""", ItemKind.FileEdit, "Edit n.ipynb", "/work/n.ipynb", "print(1)")]
    [InlineData("Bash", """{ "command": "dotnet test\nmore", "description": "Run the tests" }""", ItemKind.Command, "Run dotnet test", "dotnet test\nmore", "dotnet test\nmore")]
    [InlineData("Grep", """{ "pattern": "TODO", "path": "/work/src", "glob": "*.cs" }""", ItemKind.Search, "Search TODO", "TODO", "TODO in src *.cs")]
    [InlineData("Glob", """{ "pattern": "**/*.cs" }""", ItemKind.Search, "Search **/*.cs", "**/*.cs", "**/*.cs")]
    [InlineData("WebFetch", """{ "url": "https://example.com", "prompt": "Read" }""", ItemKind.Web, "Fetch https://example.com", "https://example.com", "https://example.com\nRead")]
    [InlineData("WebSearch", """{ "query": "avalonia" }""", ItemKind.Web, "Search the web for avalonia", "avalonia", "avalonia")]
    [InlineData("mcp__github__list", """{ }""", ItemKind.Mcp, "mcp__github__list", "mcp__github__list", "")]
    [InlineData("mcp__github__issue", """{ "number": 7 }""", ItemKind.Mcp, "mcp__github__issue", "mcp__github__issue", """{"number":7}""")]
    [InlineData("Agent", """{ "description": "Explore", "prompt": "Look", "subagent_type": "Explore" }""", ItemKind.Subagent, "Subagent: Explore", "Explore", "Look")]
    [InlineData("Read", """{ "file_path": "/work/a.txt" }""", ItemKind.Other, "Read a.txt", "/work/a.txt", "a.txt")]
    [InlineData("ToolSearch", """{ "query": "select:mcp__avala__canvas", "max_results": 1 }""", ItemKind.Other, "Load mcp__avala__canvas", "ToolSearch", "select:mcp__avala__canvas")]
    public void EveryToolUseIsAnItemOfItsKindWithATitleItsInputAndTheTargetItsPermissionNames(string tool, string input, ItemKind kind, string title, string target, string details)
    {
        var onHost = Cli.OnHost(input);
        var talk = new Talk().Begin().Receive(Cli.ToolUse("t1", tool, onHost)).Receive(Cli.Prompt("r1", tool, onHost, "t1"));

        Assert.Equal(
            new ItemStarted(talk.Session, talk.Turn, new ItemId("t1"), kind, title) { Input = details.Length == 0 ? Option<string>.None : details },
            talk.Events.OfType<ItemStarted>().Single());
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
    public void TheTaskToolsUpdateThePlanAsTasksAreCreatedStartedFinishedAndDeleted()
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("c1", "TaskCreate", """{ "subject": "Write", "description": "Write the file", "activeForm": "Writing" }"""),
            Cli.ToolUse("c2", "TaskCreate", """{ "subject": "Test", "description": "Run the tests" }"""),
            Cli.ToolResult("c1", "Task #1 created successfully: Write"),
            Cli.ToolResult("c2", "Task #2 created successfully: Test"),
            Cli.ToolUse("u1", "TaskUpdate", """{ "taskId": "1", "status": "in_progress" }"""),
            Cli.ToolUse("u2", "TaskUpdate", """{ "taskId": "1", "status": "completed" }"""),
            Cli.ToolUse("u3", "TaskUpdate", """{ "taskId": "2", "status": "deleted" }"""),
            Cli.ToolUse("l1", "TaskList", "{}"));

        Assert.Equal(
            [
                "Write Pending",
                "Write Pending, Test Pending",
                "Write InProgress, Test Pending",
                "Write Done, Test Pending",
                "Write Done",
            ],
            talk.Events.OfType<PlanUpdated>().Select(plan => string.Join(", ", plan.Steps.Select(step => $"{step.Title} {step.Status}"))));
        Assert.Empty(talk.Events.OfType<ItemStarted>());
    }

    [Fact]
    public void ATaskListReplacesTheWholePlanWithTheTasksItReturns()
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("c1", "TaskCreate", """{ "subject": "Write" }"""),
            Cli.ToolResult("c1", "Task #1 created successfully: Write"),
            Cli.ToolUse("l1", "TaskList", "{}"),
            Cli.ToolResult("l1", "#2 [completed] Read the code\n#3 [in_progress] Write the change\n#4 [pending] Run the tests [blocked by #3]"),
            Cli.ToolUse("u1", "TaskUpdate", """{ "taskId": "3", "status": "completed" }"""),
            Cli.ToolUse("l2", "TaskList", "{}"),
            Cli.ToolResult("l2", "No tasks found"));

        Assert.Equal(
            [
                "Write Pending",
                "Read the code Done, Write the change InProgress, Run the tests Pending",
                "Read the code Done, Write the change Done, Run the tests Pending",
                string.Empty,
            ],
            talk.Events.OfType<PlanUpdated>().Select(plan => string.Join(", ", plan.Steps.Select(step => $"{step.Title} {step.Status}"))));
    }

    [Theory]
    [InlineData("The task list could not be read.", false)]
    [InlineData("#1 [pending] Write", true)]
    public void ATaskListThatFailsOrIsNotAListLeavesThePlan(string output, bool isError)
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("c1", "TaskCreate", """{ "subject": "Write" }"""),
            Cli.ToolResult("c1", "Task #1 created successfully: Write"),
            Cli.ToolUse("l1", "TaskList", "{}"),
            Cli.ToolResult("l1", output, isError));

        Assert.Equal(["Write"], talk.Events.OfType<PlanUpdated>().Select(plan => string.Join(", ", plan.Steps.Select(step => step.Title))));
    }

    [Fact]
    public void AResumedConversationKeepsThePlanItsTasksBuiltBeforeTheRestart()
    {
        var talk = new Talk();
        talk.Conversation.Recalling(
        [
            Cli.ToolUse("c1", "TaskCreate", """{ "subject": "Write" }"""),
            Cli.ToolUse("c2", "TaskCreate", """{ "subject": "Test" }"""),
            Cli.ToolResult("c1", "Task #1 created successfully: Write"),
            Cli.ToolResult("c2", "Task #2 created successfully: Test"),
            Cli.ToolUse("u1", "TaskUpdate", """{ "taskId": "1", "status": "completed" }"""),
            Cli.Nested(Cli.ToolUse("s1", "TaskCreate", """{ "subject": "A subagent's step" }"""), "a1"),
            Cli.Parse("""{ "type": "assistant", "isSidechain": true, "message": { "content": [ { "type": "tool_use", "id": "s2", "name": "TodoWrite", "input": { "todos": [ { "content": "Sidechain", "status": "pending" } ] } } ] } }"""),
        ]);

        talk.Begin().Receive(Cli.ToolUse("u2", "TaskUpdate", """{ "taskId": "2", "status": "in_progress" }"""));

        Assert.Equal(
            [new PlanStep("Write", PlanStepStatus.Done), new PlanStep("Test", PlanStepStatus.InProgress)],
            Assert.Single(talk.Events.OfType<PlanUpdated>()).Steps);
    }

    [Fact]
    public void ATaskThatFailsToBeCreatedLeavesThePlan()
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("c1", "TaskCreate", """{ "subject": "Write" }"""),
            Cli.ToolResult("c1", "The task list is full.", isError: true));

        Assert.Empty(talk.Events.OfType<PlanUpdated>().Last().Steps);
    }

    [Fact]
    public void ASubagentsTextGrowsItsOwnItemWhileItsToolsAreItemsOfTheirOwnAndItsPlanIsNotTheJobs()
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("a1", "Agent", """{ "description": "Explore", "prompt": "Find the greeting", "subagent_type": "Explore" }"""),
            Cli.Nested(Cli.Parse("""{ "type": "stream_event", "event": { "type": "message_start", "message": { "id": "sub-1" } } }"""), "a1"),
            Cli.Nested(Cli.Said("sub-1", "text", "Reading the repository."), "a1"),
            Cli.Nested(Cli.ToolUse("r1", "Read", """{ "file_path": "/work/GREETING.md" }"""), "a1"),
            Cli.Nested(Cli.ToolUse("p1", "TodoWrite", """{ "todos": [ { "content": "Look", "status": "pending" } ] }"""), "a1"),
            Cli.Nested(Cli.ToolUse("p2", "TaskCreate", """{ "subject": "Look" }"""), "a1"),
            Cli.Nested(Cli.ToolResult("p2", "Task #1 created successfully: Look"), "a1"),
            Cli.Nested(Cli.ToolUse("p3", "TaskUpdate", """{ "taskId": "1", "status": "completed" }"""), "a1"),
            Cli.Nested(Cli.ToolResult("r1", "# Hello"), "a1"),
            Cli.Nested(Cli.Said("sub-2", "text", "The greeting is a heading."), "a1"),
            Cli.ToolResult("a1", "The greeting is a heading."));
        var subagent = new ItemId("a1");

        Assert.Equal([ItemKind.Subagent, ItemKind.Other], talk.Events.OfType<ItemStarted>().Select(started => started.Kind));
        Assert.Equal(
            ["Reading the repository.", "\n\nThe greeting is a heading."],
            talk.Events.OfType<ItemProgressed>().Where(progressed => progressed.Item == subagent).Select(progressed => progressed.Text));
        Assert.Equal("# Hello", talk.Events.OfType<ItemProgressed>().Single(progressed => progressed.Item.Value == "r1").Text);
        Assert.Empty(talk.Events.OfType<PlanUpdated>());
        Assert.Equal([ItemOutcome.Succeeded, ItemOutcome.Succeeded], talk.Events.OfType<ItemCompleted>().Select(completed => completed.Outcome));
    }

    [Fact]
    public void AToolSearchResultListsTheToolsItLoaded()
    {
        var talk = new Talk().Begin().Receive(
            Cli.ToolUse("s1", "ToolSearch", """{ "query": "select:mcp__avala__canvas" }"""),
            Cli.Parse("""
                { "type": "user", "message": { "role": "user", "content": [ { "type": "tool_result", "tool_use_id": "s1",
                  "content": [ { "type": "tool_reference", "tool_name": "mcp__avala__canvas" } ] } ] } }
                """));

        Assert.Equal("mcp__avala__canvas", talk.Events.OfType<ItemProgressed>().Single().Text);
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
    public void ARateLimitEventOutsideATurnReportsNothing()
    {
        var talk = new Talk().Receive(Cli.RateLimit());

        Assert.Empty(talk.Events);
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
