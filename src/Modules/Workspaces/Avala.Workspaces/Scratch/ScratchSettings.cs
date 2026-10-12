namespace Avala.Workspaces.Scratch;

internal sealed record ScratchSettings(string Folder)
{
    public Func<string> Ids { get; init; } = () => Guid.NewGuid().ToString("N")[..6];
}
