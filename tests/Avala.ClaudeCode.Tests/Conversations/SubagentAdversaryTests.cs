using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class SubagentAdversaryTests
{
    [Fact]
    public void AStoppedSubagentsUnansweredPermissionNoLongerHoldsBackTheQuestionOfASubagentStillWorking()
    {
        var talk = Launched(new Talk(), "a1", "k1", "a2", "k2");
        talk.Receive(
            Cli.Nested(Cli.ToolUse("n1", "Bash", """{ "command": "rm -rf build" }"""), "a1"),
            Cli.Prompt("r1", "Bash", """{ "command": "rm -rf build" }""", "n1"),
            Cli.Nested(Cli.ToolUse("n2", "Bash", """{ "command": "npm install" }"""), "a2"),
            Cli.Prompt("r2", "Bash", """{ "command": "npm install" }""", "n2"));
        talk.Drain();

        talk.Receive(Notification("k1", "a1", "stopped"));

        Assert.Equal(
            ["withdrawn n1", "asks n2"],
            talk.Events.Select(happened => happened switch
            {
                RequestWithdrawn withdrawn => $"withdrawn {withdrawn.Item.Value}",
                PermissionRequested requested => $"asks {requested.Item.Value}",
                _ => string.Empty,
            }).Where(said => said.Length > 0));
    }

    [Fact]
    public void ABackgroundSubagentStillWorkingWhenItsTurnFailsIsEndedByItsOwnNotificationAndNotShownAgainAsAnotherTask()
    {
        var talk = new Talk().Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse("a1", "Agent", """{ "description": "Survey", "prompt": "Survey billing", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started("k1", "a1"),
            Cli.ToolResult("a1", "Async agent launched successfully."));
        talk.Receive(Cli.RefreshFailed());
        talk.Receive(Cli.Lifecycle(talk.Command, "completed"), Notification("k1", "a1", "completed"), Cli.Said("m2", "text", "The survey came back."), Cli.Result(0.009m));

        Assert.Equal(
            (true, false),
            (talk.Events.OfType<ItemCompleted>().Any(completed => completed.Item == new ItemId("a1")), talk.Events.OfType<ItemStarted>().Any(started => started.Item == new ItemId("task:k1"))));
    }

    private static Talk Launched(Talk talk, string first, string firstTask, string second, string secondTask)
    {
        talk.Begin();
        talk.Receive(
            Cli.Lifecycle(talk.Command, "queued"),
            Cli.Lifecycle(talk.Command, "started"),
            Cli.Init(),
            Cli.ToolUse(first, "Agent", """{ "description": "Clean", "prompt": "Clean the build", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(firstTask, first),
            Cli.ToolResult(first, "Async agent launched successfully."),
            Cli.ToolUse(second, "Agent", """{ "description": "Install", "prompt": "Install the packages", "subagent_type": "general-purpose", "run_in_background": true }"""),
            Started(secondTask, second),
            Cli.ToolResult(second, "Async agent launched successfully."),
            Cli.Said("m1", "text", "launched"),
            Cli.Result(0.006m),
            Cli.Lifecycle(talk.Command, "completed"));
        talk.Drain();

        return talk;
    }

    private static JsonNode Started(string task, string tool) =>
        Cli.Parse($$"""{ "type": "system", "subtype": "task_started", "task_id": "{{task}}", "tool_use_id": "{{tool}}", "description": "Survey", "subagent_type": "general-purpose", "is_backgrounded": true }""");

    private static JsonNode Notification(string task, string tool, string status) =>
        Cli.Parse($$"""{ "type": "system", "subtype": "task_notification", "task_id": "{{task}}", "tool_use_id": "{{tool}}", "status": "{{status}}", "summary": "Survey" }""");
}
