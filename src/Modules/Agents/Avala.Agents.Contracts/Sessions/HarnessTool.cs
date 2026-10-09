namespace Avala.Agents.Contracts.Sessions;

public enum ToolSurface
{
    Canvas,
    Executed,
}

public sealed record HarnessTool(string Name, string Description, string InputSchema, ToolSurface Surface);

public sealed record ToolResult(ItemId Item, string Content)
{
    public bool IsError { get; init; }
}
