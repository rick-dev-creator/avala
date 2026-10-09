using Avala.Runtime.Events;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Runtime.Tests.Events;

public sealed class EventBusTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeliversAnEventToEveryHandlerAsync()
    {
        var first = new RecordingHandler(expected: 1);
        var second = new RecordingHandler(expected: 1);
        await using var running = Run(first, second);

        await running.Bus.PublishAsync(new Pinged(1), Cancellation);

        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Patience, Cancellation);
        Assert.Equal([1], first.Received);
        Assert.Equal([1], second.Received);
    }

    [Fact]
    public async Task DeliversEventsToEachHandlerInPublicationOrderAsync()
    {
        var first = new RecordingHandler(expected: 3);
        var second = new RecordingHandler(expected: 3);
        await using var running = Run(first, second);

        foreach (var sequence in new[] { 1, 2, 3 })
        {
            await running.Bus.PublishAsync(new Pinged(sequence), Cancellation);
        }

        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Patience, Cancellation);
        Assert.Equal([1, 2, 3], first.Received);
        Assert.Equal([1, 2, 3], second.Received);
    }

    [Fact]
    public async Task AHandlerOfSeveralEventTypesReceivesThemAllInPublicationOrderAsync()
    {
        var handler = new TwoEventHandler(expected: 4);
        await using var running = Run(services => services
            .AddSingleton<IHandle<Pinged>>(handler)
            .AddSingleton<IHandle<Ponged>>(handler));

        await running.Bus.PublishAsync(new Pinged(1), Cancellation);
        await running.Bus.PublishAsync(new Ponged(2), Cancellation);
        await running.Bus.PublishAsync(new Pinged(3), Cancellation);
        await running.Bus.PublishAsync(new Ponged(4), Cancellation);

        await handler.Completion.WaitAsync(Patience, Cancellation);
        Assert.Equal(["ping 1", "pong 2", "ping 3", "pong 4"], handler.Received);
    }

    [Fact]
    public async Task ASlowHandlerDelaysNeitherTheOtherHandlersNorTheSubscribersAsync()
    {
        var slow = new BlockingHandler();
        var other = new RecordingHandler(expected: 2);
        await using var running = Run(slow, other);
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var events = running.Bus.SubscribeAsync<Pinged>(subscription.Token).GetAsyncEnumerator(Cancellation);

        await running.Bus.PublishAsync(new Pinged(1), Cancellation);
        await slow.Started.WaitAsync(Patience, Cancellation);
        await running.Bus.PublishAsync(new Pinged(2), Cancellation);

        await other.Completion.WaitAsync(Patience, Cancellation);
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Patience, Cancellation));
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Patience, Cancellation));
        Assert.Equal([1, 2], other.Received);
        Assert.Equal(new Pinged(2), events.Current);
        Assert.False(slow.Ended);
        slow.Release();
    }

    [Fact]
    public async Task StoppingCancelsTheRunningHandlersAndWaitsForThemToEndAsync()
    {
        var slow = new BlockingHandler();
        var running = Run(slow);
        await running.Bus.PublishAsync(new Pinged(1), Cancellation);
        await slow.Started.WaitAsync(Patience, Cancellation);

        await running.DisposeAsync().AsTask().WaitAsync(Patience, Cancellation);

        Assert.True(slow.Ended);
    }

    [Fact]
    public async Task KeepsDeliveringWhenAHandlerFailsAsync()
    {
        var survivor = new RecordingHandler(expected: 2);
        await using var running = Run(new FailingHandler(), survivor);

        await running.Bus.PublishAsync(new Pinged(1), Cancellation);
        await running.Bus.PublishAsync(new Pinged(2), Cancellation);

        await survivor.Completion.WaitAsync(Patience, Cancellation);
        Assert.Equal([1, 2], survivor.Received);
    }

    [Fact]
    public async Task IgnoresEventsPublishedAfterTheBusStopsAsync()
    {
        var handler = new RecordingHandler(expected: 1);
        var running = Run(handler);
        await running.DisposeAsync();

        await running.Bus.PublishAsync(new Pinged(1), Cancellation);

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task StreamsEventsToASubscriberAsync()
    {
        await using var running = Run();
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var events = running.Bus.SubscribeAsync<Pinged>(subscription.Token).GetAsyncEnumerator(Cancellation);

        await running.Bus.PublishAsync(new Pinged(9), Cancellation);

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(Patience, Cancellation));
        Assert.Equal(new Pinged(9), events.Current);
    }

    [Fact]
    public async Task EndsASubscriptionWhenItIsCancelledAsync()
    {
        await using var running = Run();
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var events = running.Bus.SubscribeAsync<Pinged>(subscription.Token).GetAsyncEnumerator(Cancellation);

        await subscription.CancelAsync();

        Assert.False(await events.MoveNextAsync().AsTask().WaitAsync(Patience, Cancellation));
    }

    private static RunningBus Run(params IHandle<Pinged>[] handlers) =>
        Run(services =>
        {
            foreach (var handler in handlers)
            {
                services.AddSingleton(handler);
            }
        });

    private static RunningBus Run(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        return new RunningBus(new EventBus(services.BuildServiceProvider(), NullLogger<EventBus>.Instance));
    }
}
