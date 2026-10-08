using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class MutableLedgerEvent : IDomainEvent
{
    public int Amount { get; set; }
}
