using System.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Testing;

public sealed class EventWatch<TEvent>(
    IAsyncEnumerable<TEvent> events,
    IAsyncEnumerable<IIntegrationEvent> steps,
    Func<CancellationToken, Task<string>> circumstances,
    CancellationToken cancellationToken)
    where TEvent : IIntegrationEvent
{
    private const int RememberedSteps = 6;

    private static TimeSpan Patience => TimeSpan.FromSeconds(30);

    private static TimeSpan Ceiling => TimeSpan.FromMinutes(5);

    private readonly IAsyncEnumerator<TEvent> pending = events.GetAsyncEnumerator(cancellationToken);
    private readonly IAsyncEnumerator<IIntegrationEvent> progress = steps.GetAsyncEnumerator(cancellationToken);
    private readonly Stopwatch watching = Stopwatch.StartNew();
    private readonly Queue<string> remembered = new();
    private Option<Task<bool>> step;
    private bool stepsEnded;

    public EventWatch(IAsyncEnumerable<TEvent> events, IAsyncEnumerable<IIntegrationEvent> steps, CancellationToken cancellationToken)
        : this(events, steps, _ => Task.FromResult(string.Empty), cancellationToken)
    {
    }

    public EventWatch(IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken)
        : this(events, AsyncEnumerable.Empty<IIntegrationEvent>(), cancellationToken)
    {
    }

    public ProcessesInFlight Processes { get; init; } = ProcessesInFlight.None;

    public TimeProvider Clock { get; init; } = TimeProvider.System;

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

        var waiting = stepping.Match<Task>(armed => Task.WhenAny(next, armed), () => next);

        if (!await PatientlyAsync(waiting))
        {
            var last = seen.Count > 0 ? $"the last was {seen[^1]}" : "none arrived";

            throw new TimeoutException(
                $"No {typeof(TEvent).Name} came {Silence()} after {seen.Count} that did not match; {last}; {Steps()}{await circumstances(cancellationToken)}");
        }

        await waiting;

        if (next.IsCompleted)
        {
            return false;
        }

        var advanced = await stepping.Match(armed => armed, () => Task.FromResult(false));
        stepsEnded = !advanced;

        if (advanced)
        {
            Remember(progress.Current);
        }

        step = Option<Task<bool>>.None;

        return true;
    }

    private async Task<bool> PatientlyAsync(Task waiting)
    {
        var started = Clock.GetTimestamp();

        while (!waiting.IsCompleted)
        {
            var waited = Clock.GetElapsedTime(started);
            var quiet = Processes.Running ? TimeSpan.Zero : Min(waited, Processes.Quiet);
            var left = Min(Patience - quiet, Ceiling - waited);

            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            await waiting.WaitAsync(left, Clock, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return true;
    }

    private string Silence() =>
        Processes.Running
            ? $"in the {Ceiling.TotalMinutes:0} minutes a process kept running"
            : $"within {Patience.TotalSeconds:0} seconds of the last step or process";

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;

    private void Remember(IIntegrationEvent reached)
    {
        remembered.Enqueue($"{reached} at {Seconds(watching.Elapsed)}");

        if (remembered.Count > RememberedSteps)
        {
            _ = remembered.Dequeue();
        }
    }

    private string Steps() =>
        remembered.Count == 0
            ? $"no step came in the {Seconds(watching.Elapsed)} watched"
            : $"the last steps were {string.Join(", ", remembered)} of the {Seconds(watching.Elapsed)} watched";

    private static string Seconds(TimeSpan elapsed) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:0.0} s");
}
