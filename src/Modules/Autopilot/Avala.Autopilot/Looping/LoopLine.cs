using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopLine(TimeProvider clock, Func<Exception, CancellationToken, Task> fault) : IAsyncDisposable
{
    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private Option<ITimer> wake;

    public void Post(Func<CancellationToken, Task> work) =>
        _ = executor.RunAsync(token => GuardedAsync(work, token), lifetime.Token);

    public Task<Result<LoopId, AutopilotError>> CommandAsync(Func<CancellationToken, Task<Result<LoopId, AutopilotError>>> command, CancellationToken cancellationToken) =>
        executor.RunAsync<Result<LoopId, AutopilotError>>(
            async token =>
            {
                try
                {
                    return await command(token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    await fault(exception, token);

                    return AutopilotError.LoopEnded;
                }
            },
            cancellationToken);

    public async Task ArmAsync(DateTimeOffset due, Func<CancellationToken, Task> work)
    {
        await DisarmAsync();
        wake = Option<ITimer>.Some(clock.CreateAlarm(() => Post(work), due));
    }

    public async Task DisarmAsync()
    {
        var armed = wake;
        wake = Option<ITimer>.None;
        await armed.Match(timer => timer.DisposeAsync().AsTask(), () => Task.CompletedTask);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await executor.RunAsync(_ => DisarmAsync(), CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task GuardedAsync(Func<CancellationToken, Task> work, CancellationToken token)
    {
        try
        {
            await work(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            await fault(exception, token);
        }
    }
}
