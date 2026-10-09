using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Governance;

internal interface IPolicyFiles
{
    ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}

internal sealed record PolicyFile(Option<FileOrigin> Origin, Result<Option<PermissionPolicy>, PolicyError> Policy)
{
    public (PermissionPolicy Policy, PolicyFileStatus File, Option<PolicyError> Error) Resolved => Policy.Match(
        found => found.Match(
            declared => (declared, PolicyFileStatus.Applied, Option<PolicyError>.None),
            () => (PermissionPolicy.BuiltIn, PolicyFileStatus.Absent, Option<PolicyError>.None)),
        rejection => (PermissionPolicy.BuiltIn, PolicyFileStatus.Rejected, Option<PolicyError>.Some(rejection)));
}
