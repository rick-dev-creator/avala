using Avala.Sdk.Events;

namespace Avala.Fixtures.Violating.Application;

public sealed record MisplacedIntegrationEvent(Guid Ledger) : IIntegrationEvent;
