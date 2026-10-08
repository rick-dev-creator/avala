using Avala.Sdk.Domain;

namespace Avala.Fixtures.Violating.Application;

public sealed record MisplacedDomainEvent(Guid Ledger) : IDomainEvent;
