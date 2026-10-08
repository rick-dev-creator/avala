using Avala.Canvas.Contracts;
using Avala.Canvas.Gallery;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Canvas.Throttling;

internal sealed class SnapshotThrottle(CanvasGallery gallery, IEventBus bus, TimeProvider clock, TimeSpan interval) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<CanvasId, Cadence> cadences = [];

    public async ValueTask ChangedAsync(CanvasId canvas, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var cadence = cadences.GetValueOrDefault(canvas, Cadence.Fresh);

            if (cadence.Flush.IsSome)
            {
                return;
            }

            var wait = cadence.Published + interval - clock.GetUtcNow();

            cadences[canvas] = wait > TimeSpan.Zero
                ? cadence with { Flush = Option<ITimer>.Some(Schedule(canvas, wait)) }
                : await PublishAsync(canvas, final: false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask FinishedAsync(CanvasId canvas, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (cadences.Remove(canvas, out var cadence))
            {
                await cadence.CancelAsync();
            }

            _ = await PublishAsync(canvas, final: true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync(CancellationToken.None);

        try
        {
            foreach (var cadence in cadences.Values)
            {
                await cadence.CancelAsync();
            }

            cadences.Clear();
        }
        finally
        {
            gate.Release();
        }
    }

    private ITimer Schedule(CanvasId canvas, TimeSpan wait) =>
        clock.CreateTimer(state => _ = FlushAsync(canvas), null, wait, Timeout.InfiniteTimeSpan);

    private async Task FlushAsync(CanvasId canvas)
    {
        await gate.WaitAsync(CancellationToken.None);

        try
        {
            if (cadences.TryGetValue(canvas, out var cadence) && cadence.Flush.IsSome)
            {
                await cadence.CancelAsync();
                cadences[canvas] = await PublishAsync(canvas, final: false, CancellationToken.None);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Cadence> PublishAsync(CanvasId canvas, bool final, CancellationToken cancellationToken)
    {
        await gallery.Snapshot(canvas).Match(
            snapshot => final || snapshot.Status == CanvasStatus.Streaming
                ? bus.PublishAsync(new CanvasUpdated(snapshot), cancellationToken).AsTask()
                : Task.CompletedTask,
            () => Task.CompletedTask);

        return new Cadence(clock.GetUtcNow(), Option<ITimer>.None);
    }

    private readonly record struct Cadence(DateTimeOffset Published, Option<ITimer> Flush)
    {
        public static Cadence Fresh => new(DateTimeOffset.MinValue, Option<ITimer>.None);

        public ValueTask CancelAsync() => Flush.Match(timer => timer.DisposeAsync(), () => ValueTask.CompletedTask);
    }
}
