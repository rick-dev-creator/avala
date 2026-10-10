using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Testing;

public sealed class EventWatch<TEvent>(IAsyncEnumerable<TEvent> events, IAsyncEnumerable<IIntegrationEvent> steps, CancellationToken cancellationToken)
    where TEvent : IIntegrationEvent
{
    private static TimeSpan Patience => TimeSpan.FromSeconds(30);

    private readonly IAsyncEnumerator<TEvent> pending = events.GetAsyncEnumerator(cancellationToken);
    private readonly IAsyncEnumerator<IIntegrationEvent> progress = steps.GetAsyncEnumerator(cancellationToken);
    private Option<Task<bool>> step;
    private bool stepsEnded;
    private string lastStep = "none";

    public EventWatch(IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken)
        : this(events, AsyncEnumerable.Empty<IIntegrationEvent>(), cancellationToken)
    {
    }

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
        var next = pending.MoveNextAsync().AsTask();

        while (await SteppedBeforeAsync(next, seen))
        {
        }

        return await next;
    }

    private async Task<bool> SteppedBeforeAsync(Task<bool> next, List<TEvent> seen)
    {
        var stepping = stepsEnded ? Option<Task<bool>>.None : step.Match(armed => armed, () => progress.MoveNextAsync().AsTask());
        step = stepping;

        try
        {
            await stepping.Match<Task>(armed => Task.WhenAny(next, armed), () => next).WaitAsync(Patience, cancellationToken);
        }
        catch (TimeoutException timeout)
        {
            var last = seen.Count > 0 ? $"the last was {seen[^1]}" : "none arrived";

            throw new TimeoutException(
                $"No {typeof(TEvent).Name} came within {Patience.TotalSeconds:0} seconds of the last step after {seen.Count} that did not match; {last}; the last step was {lastStep}.",
                timeout);
        }

        if (next.IsCompleted)
        {
            return false;
        }

        var advanced = await stepping.Match(armed => armed, () => Task.FromResult(false));
        stepsEnded = !advanced;
        lastStep = advanced ? progress.Current.ToString() ?? lastStep : lastStep;
        step = Option<Task<bool>>.None;

        return true;
    }
}
