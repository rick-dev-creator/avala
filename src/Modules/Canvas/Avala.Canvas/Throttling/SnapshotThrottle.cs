using Avala.Canvas.Contracts;
using Avala.Canvas.Gallery;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Canvas.Throttling;

internal sealed class SnapshotThrottle(CanvasGallery gallery, IEventBus bus, TimeProvider clock, TimeSpan interval) : IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly Dictionary<CanvasId, Cadence> cadences = [];

    public async ValueTask ChangedAsync(CanvasId canvas, CancellationToken cancellationToken) =>
        await serial.RunAsync(token => ChangeAsync(canvas, token), cancellationToken);

    public async ValueTask FinishedAsync(CanvasId canvas, CancellationToken cancellationToken) =>
        await serial.RunAsync(token => FinishAsync(canvas, token), cancellationToken);

    public async ValueTask IdleAsync(CancellationToken cancellationToken) =>
        await serial.RunAsync(_ => Task.CompletedTask, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.RunAsync(_ => CancelAllAsync(), CancellationToken.None);
        await serial.DisposeAsync();
    }

    private async Task ChangeAsync(CanvasId canvas, CancellationToken cancellationToken)
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

    private async Task FinishAsync(CanvasId canvas, CancellationToken cancellationToken)
    {
        if (cadences.Remove(canvas, out var cadence))
        {
            await cadence.CancelAsync();
        }

        _ = await PublishAsync(canvas, final: true, cancellationToken);
    }

    private async Task CancelAllAsync()
    {
        foreach (var cadence in cadences.Values)
        {
            await cadence.CancelAsync();
        }

        cadences.Clear();
    }

    private ITimer Schedule(CanvasId canvas, TimeSpan wait) =>
        clock.CreateTimer(state => _ = serial.RunAsync(token => FlushAsync(canvas), CancellationToken.None), null, wait, Timeout.InfiniteTimeSpan);

    private async Task FlushAsync(CanvasId canvas)
    {
        if (cadences.TryGetValue(canvas, out var cadence) && cadence.Flush.IsSome)
        {
            await cadence.CancelAsync();
            cadences[canvas] = await PublishAsync(canvas, final: false, CancellationToken.None);
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
