namespace Avala.Workspaces.Workspaces;

internal sealed record Checkpoint(int Number, CommitSha Commit, string Label);
