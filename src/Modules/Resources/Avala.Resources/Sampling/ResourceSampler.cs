using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Resources.Sampling;

internal sealed partial class ResourceSampler(IResourceSettings settings, SampleTaker taker, TimeProvider clock, ILogger<ResourceSampler> logger)
    : IStartupTask, IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private Task loop = Task.CompletedTask;
    private int disposed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var chosen = await settings.LoadAsync(cancellationToken);
        var timer = new PeriodicTimer(chosen.Sampling, clock);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        loop = Task.Run(() => LoopAsync(chosen, timer, lifetime), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await stopping.CancelAsync();
        await loop;
        stopping.Dispose();
    }

    private async Task LoopAsync(ResourceSettings chosen, PeriodicTimer timer, CancellationTokenSource lifetime)
    {
        using (lifetime)
        using (timer)
        {
            try
            {
                var measured = DateTimeOffset.MinValue;

                while (await timer.WaitForNextTickAsync(lifetime.Token))
                {
                    var now = clock.GetUtcNow();
                    var disks = now - measured >= chosen.DiskSampling;
                    measured = disks ? now : measured;
                    await SampleAsync(now, disks, lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
        }
    }

    private async Task SampleAsync(DateTimeOffset now, bool disks, CancellationToken cancellationToken)
    {
        try
        {
            await taker.TakeAsync(now, disks, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogSampleFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sampling the resources failed")]
    private partial void LogSampleFailed(Exception exception);
}
