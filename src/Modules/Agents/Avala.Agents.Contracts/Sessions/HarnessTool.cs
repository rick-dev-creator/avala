namespace Avala.Agents.Contracts.Sessions;

public enum ToolSurface
{
    Canvas,
    Executed,
}

public sealed record HarnessTool(string Name, string Description, string InputSchema, ToolSurface Surface);

public interface IHarnessToolGate
{
    ValueTask<bool> OffersAsync(HarnessTool tool, string workingDirectory, CancellationToken cancellationToken);
}

public sealed record ToolResult(ItemId Item, string Content)
{
    public bool IsError { get; init; }
}

public sealed record PreviewOffer(string PortVariable, string PortsVariable);
