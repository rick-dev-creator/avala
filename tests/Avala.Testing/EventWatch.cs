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

        while (await NextAsync(seen))
        {
            seen.Add(pending.Current);

            if (match(pending.Current))
            {
                return seen;
            }
        }

        throw new InvalidOperationException($"The feed of {typeof(TEvent).Name} ended before the expected event.");
    }

    private async Task<bool> NextAsync(List<TEvent> seen)
    {
        try
        {
            return await pending.MoveNextAsync().AsTask().WaitAsync(Patience, cancellationToken);
        }
        catch (TimeoutException timeout)
        {
            var last = seen.Count > 0 ? $"the last was {seen[^1]}" : "none arrived";

            throw new TimeoutException($"No {typeof(TEvent).Name} came within {Patience.TotalSeconds:0} seconds after {seen.Count} that did not match; {last}.", timeout);
        }
    }
}
