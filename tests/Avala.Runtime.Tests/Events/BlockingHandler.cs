using Avala.Sdk.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed class BlockingHandler : IHandle<Pinged>
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;

    public bool Ended { get; private set; }

    public void Release() => release.TrySetResult();

    public async ValueTask HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken)
    {
        started.TrySetResult();

        try
        {
            await release.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Ended = true;
        }
    }
}
