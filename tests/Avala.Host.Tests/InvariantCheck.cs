using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal static class InvariantCheck
{
    private const int SettlingRounds = 4;

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    public static async Task<IReadOnlyList<string>> SettledAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> broken = [];

        for (var round = 0; round < SettlingRounds && (round == 0 || broken.Count > 0) && !cancellationToken.IsCancellationRequested; round++)
        {
            await QuietAsync(services, cancellationToken);
            broken = Invariants.BrokenIn(await Scene.ReadAsync(services, services.GetRequiredService<TurnLedger>(), cancellationToken));
        }

        return broken;
    }

    public static async Task<IReadOnlyList<string>> InstantAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Invariants.InstantlyBrokenIn(await Scene.ReadAsync(services, services.GetRequiredService<TurnLedger>(), cancellationToken));

    private static async Task QuietAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var feed = services.GetRequiredService<IEventFeed>();
        var ended = services.GetRequiredService<IEndedJobs>();
        await feed.DeliveredAsync(cancellationToken).WaitAsync(HangGuard, cancellationToken);

        foreach (var job in await services.GetRequiredService<IJobCatalog>().ListAsync(cancellationToken))
        {
            _ = await ended.WhileEndedAsync(job.Job, _ => Task.CompletedTask, cancellationToken).AsTask().WaitAsync(HangGuard, cancellationToken);
        }

        await feed.DeliveredAsync(cancellationToken).WaitAsync(HangGuard, cancellationToken);
    }
}
