using Avala.Sdk.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed class FailingHandler : IHandle<Pinged>
{
    public ValueTask HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken) =>
        ValueTask.FromException(new InvalidOperationException("handler failure"));
}
