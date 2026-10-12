using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IScratchRepositories
{
    string Folder { get; }

    bool Holds(string repository);

    ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken);
}
