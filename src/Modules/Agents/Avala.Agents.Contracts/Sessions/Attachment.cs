namespace Avala.Agents.Contracts.Sessions;

public sealed record Attachment(string Name, string MediaType, string Path)
{
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.Ordinal);

    public bool IsText => MediaType.StartsWith("text/", StringComparison.Ordinal);
}
