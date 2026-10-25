using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class DiscoveryTerms(IDiscoverySettings settings, IRepositoryPolicies policies)
{
    public ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken) => settings.DiscoversAsync(cancellationToken);

    public async Task<bool> RunsAsync(string repository, CancellationToken cancellationToken) =>
        (await policies.OfRepositoryAsync(repository, cancellationToken)).Autonomy == Autonomy.Autonomous;
}
