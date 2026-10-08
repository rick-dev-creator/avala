using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class VoidOperationLedger : IAggregateRoot<LedgerId>
{
    private VoidOperationLedger(LedgerId id) => Id = id;

    public LedgerId Id { get; }

    public bool IsClosed { get; private set; }

    public static Result<VoidOperationLedger, LedgerError> Open(LedgerId id) => new VoidOperationLedger(id);

    public void Close() => IsClosed = true;
}
