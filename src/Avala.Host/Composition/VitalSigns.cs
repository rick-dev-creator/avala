using System.Diagnostics;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Host.Composition;

internal sealed partial class VitalSigns(LogFile log, IUiDispatcher ui, TimeProvider clock)
{
    public const string Category = "Avala.Vitals";

    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    private const long Megabyte = 1024 * 1024;

    private readonly ILogger logger = log.CreateLogger(Category);
    private long waitingSince;

    public ITimer Follow(CancellationToken cancellationToken) =>
        clock.CreateTimer(_ => Take(cancellationToken), null, Every, Every);

    private void Take(CancellationToken cancellationToken)
    {
        var since = Volatile.Read(ref waitingSince);
        using var process = Process.GetCurrentProcess();
        var heap = GC.GetTotalMemory(forceFullCollection: false);
        var threads = process.Threads.Count;
        var interfaceWait = since == 0 ? 0 : (long)clock.GetElapsedTime(since).TotalSeconds;
        Alive(logger, process.WorkingSet64 / Megabyte, heap / Megabyte, threads, ThreadPool.PendingWorkItemCount, interfaceWait);

        if (since == 0)
        {
            Volatile.Write(ref waitingSince, clock.GetTimestamp());
            _ = ProbeAsync(cancellationToken);
        }
    }

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ui.InvokeAsync(() => { }, cancellationToken);
            Volatile.Write(ref waitingSince, 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Avala is alive with {WorkingSet} MB in memory, {Heap} MB of it managed, {Threads} threads, {PendingWork} work items queued and its interface waited on for {InterfaceWait} s")]
    private static partial void Alive(ILogger logger, long workingSet, long heap, int threads, long pendingWork, long interfaceWait);
}
