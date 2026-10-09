namespace Avala.Agents.Contracts.Sessions;

public enum ToolSurface
{
    Canvas,
}

public sealed record HarnessTool(string Name, string Description, string InputSchema, ToolSurface Surface);
