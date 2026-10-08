using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed class PublicConstructorLedger(LedgerId id) : IAggregateRoot<LedgerId>
{
    public LedgerId Id { get; } = id;
}
