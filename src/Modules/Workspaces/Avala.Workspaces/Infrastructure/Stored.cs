using System.Text.Json;
using Avala.Sdk;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Infrastructure;

internal static class Stored
{
    public static string Write(WorkspaceLocation location) => JsonSerializer.Serialize(new[] { location.Repository, location.Path });

    public static WorkspaceLocation Location(string json) =>
        JsonSerializer.Deserialize<string[]>(json) is [var repository, var path]
            ? Restore(WorkspaceLocation.Create(repository, path))
            : throw new InvalidDataException("Stored workspace location is invalid");

    public static BranchName Branch(string name) => Restore(BranchName.Create(name));

    public static CommitSha Commit(string sha) => Restore(CommitSha.Create(sha));

    private static T Restore<T>(Result<T, WorkspaceError> stored) =>
        stored.Match(value => value, error => throw new InvalidDataException($"Stored workspace data is invalid: {error}"));
}
