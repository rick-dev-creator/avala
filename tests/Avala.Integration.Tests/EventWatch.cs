using Avala.Sdk.Events;

namespace Avala.Integration.Tests;

internal sealed class EventWatch<TEvent>(IAsyncEnumerable<TEvent> events)
    where TEvent : IIntegrationEvent
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly IAsyncEnumerator<TEvent> pending = events.GetAsyncEnumerator();

    public async Task<TEvent> UntilAsync(Func<TEvent, bool> match)
    {
        while (await pending.MoveNextAsync().AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken))
        {
            if (match(pending.Current))
            {
                return pending.Current;
            }
        }

        throw new InvalidOperationException($"The feed of {typeof(TEvent).Name} ended before the expected event.");
    }
}
