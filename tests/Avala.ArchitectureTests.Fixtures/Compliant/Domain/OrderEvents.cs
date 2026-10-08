using Avala.Sdk.Domain;

namespace Avala.Fixtures.Compliant.Domain;

public sealed record OrderPlaced(OrderId Order) : IDomainEvent;

public sealed record LineAdded(OrderId Order, string Line) : IDomainEvent;
