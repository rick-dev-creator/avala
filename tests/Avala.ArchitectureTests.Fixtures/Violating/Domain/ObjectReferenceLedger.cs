using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class ObjectReferenceLedger : IAggregateRoot<LedgerId>
{
    private readonly PublicConstructorLedger parent;

    private ObjectReferenceLedger(LedgerId id, PublicConstructorLedger parent)
    {
        Id = id;
        this.parent = parent;
    }

    public LedgerId Id { get; }

    public LedgerId ParentId => parent.Id;

    public static Result<ObjectReferenceLedger, LedgerError> Open(LedgerId id, PublicConstructorLedger parent) =>
        new ObjectReferenceLedger(id, parent);
}
