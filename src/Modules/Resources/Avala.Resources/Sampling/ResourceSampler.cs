using System.Threading.Channels;
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
        var ticks = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
        var timer = clock.CreateTimer(_ => ticks.Writer.TryWrite(true), null, chosen.Sampling, chosen.Sampling);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        loop = Task.Run(() => LoopAsync(chosen, timer, ticks.Reader, lifetime), CancellationToken.None);
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

    private async Task LoopAsync(ResourceSettings chosen, ITimer timer, ChannelReader<bool> ticks, CancellationTokenSource lifetime)
    {
        using (lifetime)
        await using (timer)
        {
            try
            {
                var measured = DateTimeOffset.MinValue;

                await foreach (var _ in ticks.ReadAllAsync(lifetime.Token))
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
