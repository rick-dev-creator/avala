using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Domain;

public sealed record Posted(LedgerId Ledger) : IDomainEvent;
