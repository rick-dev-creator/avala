using Avala.Runtime.Events;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Runtime;

public static partial class RuntimeHost
{
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(10);

    extension(IServiceProvider services)
    {
        public Task RunAsync(CancellationToken cancellationToken) =>
            Task.Run(() => services.HostAsync(cancellationToken), CancellationToken.None);

        private async Task HostAsync(CancellationToken cancellationToken)
        {
            var bus = services.GetRequiredService<EventBus>();
            using var stopped = new CancellationTokenSource();
            var loop = bus.RunAsync(cancellationToken, stopped.Token);

            try
            {
                foreach (var stage in services.GetServices<IStartupTask>().GroupBy(task => task.Stage).OrderBy(stage => stage.Key))
                {
                    await RunStageAsync(bus, stage.Key, [.. stage], cancellationToken);
                }

                await bus.PublishAsync(new StartupCompleted(), cancellationToken);
                await cancellationToken.UntilCancelledAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            try
            {
                await services.ShutDownAsync(bus);
            }
            finally
            {
                await stopped.CancelAsync();
                await loop;
            }
        }

        private async Task ShutDownAsync(EventBus bus)
        {
            using var grace = new CancellationTokenSource(ShutdownGrace, services.GetService<TimeProvider>() ?? TimeProvider.System);

            try
            {
                foreach (var task in services.GetServices<IShutdownTask>())
                {
                    await task.StopAsync(grace.Token);
                }

                await bus.DeliveredAsync(grace.Token);
            }
            catch (OperationCanceledException) when (grace.IsCancellationRequested)
            {
                LogShutdownGraceElapsed(services.GetService<ILoggerFactory>()?.CreateLogger(typeof(RuntimeHost).FullName!) ?? NullLogger.Instance, ShutdownGrace.TotalSeconds);
            }
        }
    }

    private static async Task RunStageAsync(EventBus bus, StartupStage stage, IReadOnlyList<IStartupTask> tasks, CancellationToken cancellationToken)
    {
        if (stage == StartupStage.Open)
        {
            await Task.WhenAll(tasks.Select(task => RunTaskAsync(bus, task, cancellationToken)));

            return;
        }

        foreach (var task in tasks)
        {
            await RunTaskAsync(bus, task, cancellationToken);
        }
    }

    private static async Task RunTaskAsync(EventBus bus, IStartupTask task, CancellationToken cancellationToken)
    {
        await task.RunAsync(cancellationToken);
        await bus.PublishAsync(new StartupProgressed(task.GetType().Name, task.Stage), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The handlers did not drain within {Seconds} seconds of shutdown, so the event bus stopped with events still queued")]
    private static partial void LogShutdownGraceElapsed(ILogger logger, double seconds);
}
