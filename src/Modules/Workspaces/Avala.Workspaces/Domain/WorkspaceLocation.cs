using Avala.Sdk;

namespace Avala.Workspaces.Domain;

internal sealed record WorkspaceLocation
{
    private WorkspaceLocation(string repository, string path)
    {
        Repository = repository;
        Path = path;
    }

    public string Repository { get; }

    public string Path { get; }

    public static Result<WorkspaceLocation, WorkspaceError> Create(string repository, string path) =>
        string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(path)
            ? WorkspaceError.EmptyLocation
            : new WorkspaceLocation(repository, path);
}
