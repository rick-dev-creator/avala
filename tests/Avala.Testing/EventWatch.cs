using Avala.Sdk.Events;

namespace Avala.Testing;

public sealed class EventWatch<TEvent>(IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken)
    where TEvent : IIntegrationEvent
{
    private static TimeSpan Patience => TimeSpan.FromSeconds(30);

    private readonly IAsyncEnumerator<TEvent> pending = events.GetAsyncEnumerator(cancellationToken);

    public async Task<TEvent> UntilAsync(Func<TEvent, bool> match) => (await CollectUntilAsync(match))[^1];

    public async Task<IReadOnlyList<TEvent>> CollectUntilAsync(Func<TEvent, bool> match)
    {
        var seen = new List<TEvent>();

        while (await pending.MoveNextAsync().AsTask().WaitAsync(Patience, cancellationToken))
        {
            seen.Add(pending.Current);

            if (match(pending.Current))
            {
                return seen;
            }
        }

        throw new InvalidOperationException($"The feed of {typeof(TEvent).Name} ended before the expected event.");
    }
}
