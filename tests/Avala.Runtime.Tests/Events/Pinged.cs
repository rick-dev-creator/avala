using Avala.Sdk.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed record Pinged(int Sequence) : IIntegrationEvent;

internal sealed record Ponged(int Sequence) : IIntegrationEvent;
