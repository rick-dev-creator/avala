using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal interface IDelegationRules
{
    ValueTask<Result<Option<DelegationRules>, DelegationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken);
}
