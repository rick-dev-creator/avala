using Avala.Contests.Running;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Recovery;

internal sealed class ContestRecovery(ContestBook book, ContestDesk desk) : IStartupTask
{
    public StartupStage Stage => StartupStage.Recovery;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var losers = await book.WorkAsync<IReadOnlyList<JobId>>(
            async token =>
            {
                var found = new List<JobId>();

                foreach (var contest in book.Open())
                {
                    found.AddRange(await desk.ReconcileAsync(contest, token));
                }

                return found;
            },
            cancellationToken);

        await desk.DiscardAsync(losers, cancellationToken);
    }
}
