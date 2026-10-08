using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Events;

internal sealed partial class EventBus(IServiceProvider services, ILogger<EventBus> logger) : IEventBus, IEventFeed
{
    private readonly Channel<Func<CancellationToken, ValueTask>> queue =
        Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(new UnboundedChannelOptions { SingleReader = true });

    private ImmutableDictionary<Type, ImmutableList<object>> subscribers =
        ImmutableDictionary<Type, ImmutableList<object>>.Empty;

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        if (!queue.Writer.TryWrite(token => DispatchAsync(integrationEvent, token)))
        {
            LogPublishedAfterStop(typeof(TEvent).Name);
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var subscription = Channel.CreateUnbounded<TEvent>(new UnboundedChannelOptions { SingleReader = true });

        Subscribe(typeof(TEvent), subscription.Writer);
        cancellationToken.Register(() =>
        {
            Unsubscribe(typeof(TEvent), subscription.Writer);
            subscription.Writer.TryComplete();
        });

        return subscription.Reader.ReadAllAsync(CancellationToken.None);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var dispatch in queue.Reader.ReadAllAsync(cancellationToken))
            {
                await dispatch(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            queue.Writer.TryComplete();
        }
    }

    private async ValueTask DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IHandle<TEvent>>())
        {
            await HandleIsolatedAsync(handler, integrationEvent, cancellationToken);
        }

        foreach (var writer in subscribers.GetValueOrDefault(typeof(TEvent), []).Cast<ChannelWriter<TEvent>>())
        {
            writer.TryWrite(integrationEvent);
        }
    }

    private async ValueTask HandleIsolatedAsync<TEvent>(
        IHandle<TEvent> handler,
        TEvent integrationEvent,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        try
        {
            await handler.HandleAsync(integrationEvent, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogHandlerFailed(handler.GetType().Name, typeof(TEvent).Name, exception);
        }
    }

    private void Subscribe(Type eventType, object writer) =>
        ImmutableInterlocked.AddOrUpdate(ref subscribers, eventType, [writer], (_, writers) => writers.Add(writer));

    private void Unsubscribe(Type eventType, object writer) =>
        ImmutableInterlocked.AddOrUpdate(ref subscribers, eventType, [], (_, writers) => writers.Remove(writer));

    [LoggerMessage(Level = LogLevel.Error, Message = "{Handler} failed to handle {Event}")]
    private partial void LogHandlerFailed(string handler, string @event, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Event} was published after the event bus stopped and is ignored")]
    private partial void LogPublishedAfterStop(string @event);
}
