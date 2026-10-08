using Avala.Sdk.Events;

namespace Avala.Fixtures.Compliant.Contracts;

public sealed record OrderWasPlaced(OrderReference Order) : IIntegrationEvent;
