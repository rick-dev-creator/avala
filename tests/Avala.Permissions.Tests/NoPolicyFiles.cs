using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests;

internal sealed class NoPolicyFiles : IPolicyFiles
{
    public ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PolicyFile(Option<FileOrigin>.None, Option<PermissionPolicy>.None));
}
