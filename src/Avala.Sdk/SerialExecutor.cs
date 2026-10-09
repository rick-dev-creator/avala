using System.Threading.Channels;

namespace Avala.Sdk;

public sealed class SerialExecutor : IAsyncDisposable
{
    private readonly Channel<Func<Task>> work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task consumer;

    public SerialExecutor() => consumer = Task.Run(ConsumeAsync);

    public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        RunAsync(
            async token =>
            {
                await operation(token);

                return true;
            },
            cancellationToken);

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var outcome = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!work.Writer.TryWrite(() => SettleAsync(operation, outcome, cancellationToken)))
        {
            outcome.SetException(new ObjectDisposedException(nameof(SerialExecutor)));
        }

        return outcome.Task;
    }

    public async ValueTask DisposeAsync()
    {
        work.Writer.TryComplete();
        await consumer;
    }

    private static async Task SettleAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TaskCompletionSource<T> outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcome.TrySetResult(await operation(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome.TrySetCanceled(cancellationToken);
        }
        catch (Exception failure)
        {
            outcome.TrySetException(failure);
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var next in work.Reader.ReadAllAsync())
        {
            await next();
        }
    }
}
