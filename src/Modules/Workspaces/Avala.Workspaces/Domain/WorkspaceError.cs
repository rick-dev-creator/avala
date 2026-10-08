namespace Avala.Workspaces.Domain;

internal enum WorkspaceError
{
    InvalidBranchName,
    InvalidCommit,
    EmptyLocation,
    CannotMarkReady,
    CannotCheckpoint,
    CannotFail,
    CannotRemove,
}
