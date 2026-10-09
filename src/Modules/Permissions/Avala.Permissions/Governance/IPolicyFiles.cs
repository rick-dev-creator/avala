using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Governance;

internal interface IPolicyFiles
{
    ValueTask<Result<Option<IReadOnlyList<PolicyRule>>, PolicyError>> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}
