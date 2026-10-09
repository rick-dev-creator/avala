namespace Avala.Simulator.Playback;

internal sealed class Pacing(TimeProvider clock, TimeSpan delay)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public Task WaitAsync(CancellationToken cancellationToken) => DelayAsync(delay, cancellationToken);

    public Task DelayAsync(TimeSpan gap, CancellationToken cancellationToken) =>
        gap > TimeSpan.Zero ? Task.Delay(gap, clock, cancellationToken) : Task.CompletedTask;
}
