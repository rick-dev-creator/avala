using System.Collections.Concurrent;
using Avala.Sdk.Events;

namespace Avala.Testing;

public sealed class RecordingBus : IEventBus
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<IIntegrationEvent> published = new();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<IIntegrationEvent> Published => [.. published];

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        published.Enqueue(integrationEvent);
        Interlocked.Exchange(ref changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        return ValueTask.CompletedTask;
    }

    public async Task<TEvent> WaitForAsync<TEvent>(Func<TEvent, bool> match, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        while (true)
        {
            var signal = Volatile.Read(ref changed);

            if (published.OfType<TEvent>().FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await signal.Task.WaitAsync(Patience, cancellationToken);
        }
    }
}
