using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed partial class PlanBook
{
    public const string TodoTool = "TodoWrite";

    private const string CreateTool = "TaskCreate";

    private const string UpdateTool = "TaskUpdate";

    private readonly List<(string Id, PlanStep Step)> tasks = [];
    private readonly List<(string ToolUse, PlanStep Step)> creating = [];
    private IReadOnlyList<PlanStep> todos = [];

    public static bool Plans(string tool) => tool is TodoTool or CreateTool or UpdateTool or "TaskList" or "TaskGet";

    public Option<PlanUpdated> Used(ToolUse use, Stamp stamp)
    {
        switch (use.Name)
        {
            case TodoTool:
                todos = Telemetry.Plan(use.Input);

                return new PlanUpdated(stamp.Session, stamp.Turn, todos);
            case CreateTool:
                creating.Add((use.Id, new PlanStep(use.Input.TextOr("subject", use.Input.TextOr("description", use.Id)), PlanStepStatus.Pending)));

                return Current(stamp);
            case UpdateTool when use.Input.TextOr("taskId", string.Empty) is { Length: > 0 } id:
                var at = tasks.FindIndex(task => task.Id == id);
                var known = at >= 0 ? tasks[at].Step : new PlanStep(id, PlanStepStatus.Pending);
                var status = use.Input.TextOr("status", string.Empty);

                if (status == "deleted")
                {
                    tasks.RemoveAll(task => task.Id == id);

                    return Current(stamp);
                }

                var updated = new PlanStep(use.Input.TextOr("subject", known.Title), status.Length == 0 ? known.Status : Telemetry.Status(status));

                if (at >= 0)
                {
                    tasks[at] = (id, updated);
                }
                else
                {
                    tasks.Add((id, updated));
                }

                return Current(stamp);
            default:
                return Option<PlanUpdated>.None;
        }
    }

    public Option<PlanUpdated> Returned(string toolUse, bool failed, string output, Stamp stamp)
    {
        var at = creating.FindIndex(create => create.ToolUse == toolUse);

        if (at < 0)
        {
            return Option<PlanUpdated>.None;
        }

        var created = creating[at];
        creating.RemoveAt(at);

        if (failed)
        {
            return Current(stamp);
        }

        var id = Created().Match(output) is { Success: true } match ? match.Groups[1].Value : toolUse;

        if (tasks.All(task => task.Id != id))
        {
            tasks.Add((id, created.Step));
        }

        return Option<PlanUpdated>.None;
    }

    private PlanUpdated Current(Stamp stamp) =>
        new(stamp.Session, stamp.Turn, [.. todos, .. tasks.Select(task => task.Step), .. creating.Select(create => create.Step)]);

    [GeneratedRegex(@"^Task #(\S+) created successfully")]
    private static partial Regex Created();
}
