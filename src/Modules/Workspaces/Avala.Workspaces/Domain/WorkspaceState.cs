namespace Avala.Workspaces.Domain;

internal enum WorkspaceState
{
    Live,
    Creating,
    Ready,
    Failed,
    Removed,
}
