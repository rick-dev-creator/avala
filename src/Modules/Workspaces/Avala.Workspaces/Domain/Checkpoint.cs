namespace Avala.Workspaces.Domain;

internal sealed record Checkpoint(int Number, CommitSha Commit, string Label);
