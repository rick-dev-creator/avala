using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class LooseIdLedger : IAggregateRoot<Guid>
{
    private LooseIdLedger(Guid id) => Id = id;

    public Guid Id { get; }

    public static Result<LooseIdLedger, LedgerError> Open(Guid id) => new LooseIdLedger(id);
}
