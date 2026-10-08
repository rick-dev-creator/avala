using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class PublicSetterLedger : IAggregateRoot<LedgerId>
{
    private PublicSetterLedger(LedgerId id) => Id = id;

    public LedgerId Id { get; }

    public int Balance { get; set; }

    public static Result<PublicSetterLedger, LedgerError> Open(LedgerId id) => new PublicSetterLedger(id);
}
