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

    private const string ListTool = "TaskList";

    private const string NoTasks = "No tasks found";

    private readonly List<(string Id, PlanStep Step)> tasks = [];
    private readonly List<(string ToolUse, PlanStep Step)> creating = [];
    private readonly HashSet<string> listing = new(StringComparer.Ordinal);
    private IReadOnlyList<PlanStep> todos = [];

    public static bool Plans(string tool) => tool is TodoTool or CreateTool or UpdateTool or ListTool or "TaskGet";

    public bool Use(ToolUse use)
    {
        switch (use.Name)
        {
            case TodoTool:
                todos = Telemetry.Plan(use.Input);

                return true;
            case CreateTool:
                creating.Add((use.Id, new PlanStep(use.Input.TextOr("subject", use.Input.TextOr("description", use.Id)), PlanStepStatus.Pending)));

                return true;
            case UpdateTool when use.Input.TextOr("taskId", string.Empty) is { Length: > 0 } id:
                Update(id, use.Input.TextOr("status", string.Empty), use.Input.Text("subject"));

                return true;
            case ListTool:
                listing.Add(use.Id);

                return false;
            default:
                return false;
        }
    }

    public bool Return(string toolUse, bool failed, string output)
    {
        if (listing.Remove(toolUse))
        {
            return !failed && Listed(output);
        }

        var at = creating.FindIndex(create => create.ToolUse == toolUse);

        if (at < 0)
        {
            return false;
        }

        var created = creating[at];
        creating.RemoveAt(at);

        if (failed)
        {
            return true;
        }

        var id = Created().Match(output) is { Success: true } match ? match.Groups[1].Value : toolUse;

        if (tasks.All(task => task.Id != id))
        {
            tasks.Add((id, created.Step));
        }

        return false;
    }

    public PlanUpdated Current(Stamp stamp) =>
        new(stamp.Session, stamp.Turn, [.. todos, .. tasks.Select(task => task.Step), .. creating.Select(create => create.Step)]);

    private void Update(string id, string status, Option<string> subject)
    {
        var at = tasks.FindIndex(task => task.Id == id);

        if (status == "deleted")
        {
            tasks.RemoveAll(task => task.Id == id);

            return;
        }

        var known = at >= 0 ? tasks[at].Step : new PlanStep(id, PlanStepStatus.Pending);
        var updated = new PlanStep(subject.Match(text => text, () => known.Title), status.Length == 0 ? known.Status : Telemetry.Status(status));

        if (at >= 0)
        {
            tasks[at] = (id, updated);
        }
        else
        {
            tasks.Add((id, updated));
        }
    }

    private bool Listed(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var listed = lines.Select(line => Line().Match(line)).ToList();

        if (lines is [NoTasks] || (listed.Count > 0 && listed.All(match => match.Success)))
        {
            tasks.Clear();
            tasks.AddRange(listed.Where(match => match.Success).Select(match => (match.Groups["id"].Value, new PlanStep(match.Groups["subject"].Value, Telemetry.Status(match.Groups["status"].Value)))));

            return true;
        }

        return false;
    }

    [GeneratedRegex(@"^Task #(\S+) created successfully")]
    private static partial Regex Created();

    [GeneratedRegex(@"^#(?<id>\S+) \[(?<status>[a-z_]+)\] (?<subject>.*?)(?: \[blocked by #[^\]]*\])?$")]
    private static partial Regex Line();
}
