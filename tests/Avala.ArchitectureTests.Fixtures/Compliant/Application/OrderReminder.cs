namespace Avala.Fixtures.Compliant.Application;

public sealed class OrderReminder(TimeProvider clock)
{
    private readonly TaskCompletionSource placed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Place() => placed.TrySetResult();

    public Task UntilPlacedAsync(CancellationToken cancellationToken) => placed.Task.WaitAsync(cancellationToken);

    public async Task RemindAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        var due = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var timer = clock.CreateTimer(_ => due.TrySetResult(), null, wait, Timeout.InfiniteTimeSpan);
        await due.Task.WaitAsync(cancellationToken);
    }
}
