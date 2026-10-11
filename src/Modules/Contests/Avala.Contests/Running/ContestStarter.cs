using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Running;

internal sealed class ContestStarter(ContestBook book, IJobs jobs, CandidateConnections connections, TimeProvider clock)
{
    public async Task<Result<ContestRecord, ContestRejection>> StartAsync(ContestRequest request, int max, CancellationToken cancellationToken)
    {
        var routes = await connections.RoutesAsync(request, cancellationToken);

        return await book.WorkAsync(token => EnterAsync(request, routes, max, token), cancellationToken);
    }

    private async Task<Result<ContestRecord, ContestRejection>> EnterAsync(
        ContestRequest request,
        IReadOnlyList<Option<ConnectionName>> routes,
        int max,
        CancellationToken cancellationToken)
    {
        var entered = new List<(JobId Job, Option<ConnectionName> Connection)>();

        foreach (var route in routes)
        {
            if (!(await jobs.SubmitAsync(request.Job with { Connection = route }, cancellationToken)).TryGetValue(out var job, out _))
            {
                await WithdrawAsync(entered, cancellationToken);

                return ContestRejection.NotSubmitted;
            }

            entered.Add((job, route));
        }

        if (!Contest.Start(ContestId.New(), request.Job.RepositoryPath.Trim(), request.Job.Instruction.Trim(), entered, max, clock.GetUtcNow(), request.Spread)
            .TryGetValue(out var contest, out _))
        {
            await WithdrawAsync(entered, cancellationToken);

            return ContestRejection.TooManyCandidates;
        }

        await book.KeepAsync(contest, record => new ContestStarted(record), cancellationToken);

        return contest.Record;
    }

    private async Task WithdrawAsync(IEnumerable<(JobId Job, Option<ConnectionName> Connection)> entered, CancellationToken cancellationToken)
    {
        foreach (var (job, _) in entered)
        {
            _ = await jobs.DiscardAsync(job, cancellationToken);
        }
    }
}
