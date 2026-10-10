using Avala.Delegation.Policy;

namespace Avala.Delegation.Escalating;

internal interface IMachineDelegation
{
    ValueTask<EscalationTerms> LoadAsync(CancellationToken cancellationToken);
}
