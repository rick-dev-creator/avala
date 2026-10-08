using Avala.Runtime.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed class RunningBus : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task loop;

    public RunningBus(EventBus bus)
    {
        Bus = bus;
        loop = bus.RunAsync(lifetime.Token);
    }

    public EventBus Bus { get; }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await loop;
        lifetime.Dispose();
    }
}
