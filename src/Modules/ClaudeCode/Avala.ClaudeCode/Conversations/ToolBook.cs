using System.Text;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal enum ToolRole
{
    Work,
    Form,
    Canvas,
    Executed,
    Plan,
}

internal sealed class TrackedTool(ToolUse use, ToolRole role)
{
    public ToolUse Use { get; } = use;

    public ToolRole Role { get; } = role;

    public ItemId Item => Run == 0 ? new(Use.Id) : new($"{Use.Id}:run{Run + 1}");

    public int Run { get; init; }

    public bool Opened { get; set; }

    public bool Refused { get; set; }

    public bool Closed { get; set; }

    public bool Narrated { get; set; }

    public bool Backgrounded { get; set; }

    public bool Nested { get; set; }

    public string Task { get; set; } = string.Empty;

    public long Tokens { get; set; }

    public string Drawn { get; set; } = string.Empty;
}

internal sealed class SketchedTool(ToolUse use)
{
    public const int Stride = 512;

    public static IReadOnlyList<string> Paths { get; } = ["file_path", "notebook_path"];

    public ToolUse Use { get; } = use;

    public StringBuilder Json { get; } = new();

    public Option<ToolUse> Known { get; set; }

    public bool Abandoned { get; set; }

    public int Reported { get; set; }
}

internal sealed class ToolBook(IReadOnlyList<HarnessTool> offered)
{
    private readonly Dictionary<string, TrackedTool> tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedTool> finished = new(StringComparer.Ordinal);

    public IReadOnlyList<HarnessTool> Offered { get; } = offered;

    public IEnumerable<TrackedTool> All => tools.Values;

    public TrackedTool Track(ToolUse use)
    {
        if (!tools.TryGetValue(use.Id, out var tracked))
        {
            tracked = new TrackedTool(use, RoleOf(use.Name));
            tools[use.Id] = tracked;
        }

        return tracked;
    }

    public Option<TrackedTool> Find(string id) => tools.TryGetValue(id, out var tracked) ? tracked : Option<TrackedTool>.None;

    public bool Finished(string id, string task) =>
        (id.Length > 0 && finished.ContainsKey(id)) || (task.Length > 0 && finished.Values.Any(tool => tool.Task == task));

    public Option<TrackedTool> Rerun(string id)
    {
        var earlier = tools.TryGetValue(id, out var tracked) && tracked.Closed && tracked.Backgrounded ? tracked
            : finished.TryGetValue(id, out var ended) ? ended
            : null;

        if (earlier is null)
        {
            return Option<TrackedTool>.None;
        }

        finished.Remove(id);
        var again = new TrackedTool(earlier.Use, earlier.Role) { Run = earlier.Run + 1, Opened = true, Backgrounded = true, Task = earlier.Task };
        tools[id] = again;

        return again;
    }

    public Option<TrackedTool> OfTask(string task) => tools.Values.FirstOrDefault(tool => task.Length > 0 && tool.Task == task).ToOption();

    public Option<HarnessTool> Harness(string qualifiedName) =>
        CommandLine.Unqualified(qualifiedName).Bind(name => Offered.FirstOrDefault(tool => tool.Name == name).ToOption());

    public bool WorkingInBackground => tools.Values.Any(tool => tool.Backgrounded && !tool.Closed);

    public bool Carries(TrackedTool tool) => tool.Backgrounded || tool.Nested && tools.Values.Any(other => other.Backgrounded && !other.Closed);

    public void Forget()
    {
        foreach (var id in tools.Where(tool => tool.Value.Closed || !(tool.Value.Backgrounded || tool.Value.Nested)).Select(tool => tool.Key).ToList())
        {
            Archive(id);
        }
    }

    public void ForgetAll()
    {
        foreach (var id in tools.Keys.ToList())
        {
            Archive(id);
        }
    }

    private void Archive(string id)
    {
        if (tools.Remove(id, out var tool) && tool.Backgrounded && tool.Closed)
        {
            finished[id] = tool;
        }
    }

    private ToolRole RoleOf(string name) =>
        Harness(name).Match(
            tool => tool.Surface == ToolSurface.Canvas ? ToolRole.Canvas : ToolRole.Executed,
            () => name switch
            {
                _ when Questions.Asks(name) => ToolRole.Form,
                _ when PlanBook.Plans(name) => ToolRole.Plan,
                _ => ToolRole.Work,
            });
}
