namespace Avala.Workspaces.Contracts;

public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.CreateVersion7());
}
