using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Governance;

internal interface IPolicyFiles
{
    ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}

internal sealed record PolicyFile(Option<FileOrigin> Origin, Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> Rules);
