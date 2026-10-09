namespace Avala.Sdk.Events;

public interface IEventFeed
{
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;

    Task DeliveredAsync(CancellationToken cancellationToken);
}
