using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IRepositories
{
    ValueTask<RepositoryState> InspectAsync(string folder, CancellationToken cancellationToken);

    ValueTask<Result<string, WorkspaceFailure>> InitializeAsync(string folder, CancellationToken cancellationToken);

    ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken);
}

public enum RepositoryState
{
    Missing,
    NotARepository,
    NoCommit,
    Ready,
}
