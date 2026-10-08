using Avala.Fixtures.Violating.Other.Domain;
using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class ForeignErrorLedger : IAggregateRoot<LedgerId>
{
    private ForeignErrorLedger(LedgerId id) => Id = id;

    public LedgerId Id { get; }

    public static Result<ForeignErrorLedger, OtherError> Open(LedgerId id) => new ForeignErrorLedger(id);
}
