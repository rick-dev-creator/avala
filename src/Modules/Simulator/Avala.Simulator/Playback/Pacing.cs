namespace Avala.Simulator.Playback;

internal sealed class Pacing(TimeProvider clock, TimeSpan delay)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public Task WaitAsync(CancellationToken cancellationToken) => DelayAsync(delay, cancellationToken);

    public async Task DelayAsync(TimeSpan gap, CancellationToken cancellationToken)
    {
        if (gap <= TimeSpan.Zero)
        {
            return;
        }

        var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var timer = clock.CreateTimer(_ => elapsed.TrySetResult(), null, gap, Timeout.InfiniteTimeSpan);
        await elapsed.Task.WaitAsync(cancellationToken);
    }
}
