using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Contracts;

public interface IDelegations
{
    IReadOnlyList<DelegationRecord> All();

    IReadOnlyList<DelegationRecord> OfParent(JobId parent);

    Option<DelegationRecord> OfChild(JobId child);
}
