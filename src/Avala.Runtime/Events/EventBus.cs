using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Events;

internal sealed partial class EventBus(IServiceProvider services, ILogger<EventBus> logger) : IEventBus, IEventFeed
{
    private readonly Channel<Action<Handling>> routes =
        Channel.CreateUnbounded<Action<Handling>>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Dictionary<object, Mailbox> mailboxes = new(ReferenceEqualityComparer.Instance);

    private ImmutableDictionary<Type, ImmutableList<object>> subscribers =
        ImmutableDictionary<Type, ImmutableList<object>>.Empty;

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        if (!routes.Writer.TryWrite(handling => Route(integrationEvent, handling)))
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

    public async Task DeliveredAsync(CancellationToken cancellationToken)
    {
        var marked = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (routes.Writer.TryWrite(_ => marked.TrySetResult(Task.WhenAll(mailboxes.Values.Select(mailbox => mailbox.MarkAsync()).ToList()))))
        {
            await (await marked.Task.WaitAsync(cancellationToken)).WaitAsync(cancellationToken);
        }
    }

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken, cancellationToken);

    public async Task RunAsync(CancellationToken working, CancellationToken stopped)
    {
        var handling = new Handling(working, stopped);

        try
        {
            await foreach (var route in routes.Reader.ReadAllAsync(stopped))
            {
                route(handling);
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            routes.Writer.TryComplete();
        }

        foreach (var mailbox in mailboxes.Values)
        {
            mailbox.Close();
        }

        await Task.WhenAll(mailboxes.Values.Select(mailbox => mailbox.Delivered));
    }

    private void Route<TEvent>(TEvent integrationEvent, Handling handling)
        where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IHandle<TEvent>>())
        {
            MailboxOf(handler, handling).Post(token => HandleIsolatedAsync(handler, integrationEvent, token));
        }

        foreach (var writer in subscribers.GetValueOrDefault(typeof(TEvent), []).Cast<ChannelWriter<TEvent>>())
        {
            writer.TryWrite(integrationEvent);
        }
    }

    private Mailbox MailboxOf(object handler, Handling handling)
    {
        if (!mailboxes.TryGetValue(handler, out var mailbox))
        {
            mailbox = new Mailbox(handling);
            mailboxes.Add(handler, mailbox);
        }

        return mailbox;
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

    private readonly record struct Handling(CancellationToken Working, CancellationToken Stopped)
    {
        public CancellationToken Current => Working.IsCancellationRequested ? Stopped : Working;
    }

    private sealed class Mailbox
    {
        private readonly Channel<Func<CancellationToken, ValueTask>> letters =
            Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        public Mailbox(Handling handling) => Delivered = Task.Run(() => DeliverAsync(handling), CancellationToken.None);

        public Task Delivered { get; }

        public void Post(Func<CancellationToken, ValueTask> letter) => letters.Writer.TryWrite(letter);

        public Task MarkAsync()
        {
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!letters.Writer.TryWrite(_ => ReachedAsync(reached)))
            {
                reached.TrySetResult();
            }

            return reached.Task;
        }

        public void Close() => letters.Writer.TryComplete();

        private static ValueTask ReachedAsync(TaskCompletionSource reached)
        {
            reached.TrySetResult();

            return ValueTask.CompletedTask;
        }

        private async Task DeliverAsync(Handling handling)
        {
            try
            {
                await foreach (var letter in letters.Reader.ReadAllAsync(handling.Stopped))
                {
                    await letter(handling.Current);
                }
            }
            catch (OperationCanceledException) when (handling.Stopped.IsCancellationRequested)
            {
            }
        }
    }
}
