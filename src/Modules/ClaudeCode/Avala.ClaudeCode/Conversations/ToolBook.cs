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

    public ItemId Item => new(Use.Id);

    public bool Opened { get; set; }

    public bool Refused { get; set; }

    public bool Closed { get; set; }

    public bool Narrated { get; set; }

    public string Drawn { get; set; } = string.Empty;
}

internal sealed class ToolBook(IReadOnlyList<HarnessTool> offered)
{
    private readonly Dictionary<string, TrackedTool> tools = new(StringComparer.Ordinal);

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

    public Option<HarnessTool> Harness(string qualifiedName) =>
        CommandLine.Unqualified(qualifiedName).Bind(name => Offered.FirstOrDefault(tool => tool.Name == name).ToOption());

    public void Forget() => tools.Clear();

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
