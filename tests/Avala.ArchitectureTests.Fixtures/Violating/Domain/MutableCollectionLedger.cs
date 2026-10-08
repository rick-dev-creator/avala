using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class MutableCollectionLedger : IAggregateRoot<LedgerId>
{
    private MutableCollectionLedger(LedgerId id) => Id = id;

    public LedgerId Id { get; }

    public List<int> Entries { get; } = [];

    public static Result<MutableCollectionLedger, LedgerError> Open(LedgerId id) => new MutableCollectionLedger(id);
}
