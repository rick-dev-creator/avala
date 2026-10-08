namespace Avala.Simulator.Application;

internal sealed class Pacing(TimeProvider clock, TimeSpan delay)
{
    public Task WaitAsync(CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, clock, cancellationToken) : Task.CompletedTask;
}
