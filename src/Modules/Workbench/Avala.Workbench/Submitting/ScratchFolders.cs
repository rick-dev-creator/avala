using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Submitting;

internal sealed class ScratchFolders(IScratchRepositories scratch, IFileOpener opener)
{
    public bool Holds(string repository) => scratch.Holds(repository);

    public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken) =>
        scratch.CreateAsync(instruction.Trim(), cancellationToken);

    public async ValueTask<bool> OpenAsync(string folder, CancellationToken cancellationToken) =>
        (await opener.OpenFolderAsync(folder, cancellationToken)).IsSuccess;
}
