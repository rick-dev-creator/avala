using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Usage;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Resources.Tracking;

internal sealed class ResourceTracker(ResourceBook book, OrphanReaper reaper, PortLeases leases, WorktreeHousekeeper housekeeper)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<JobSessionRested>, IHandle<SessionEnded>, IHandle<SessionStopped>, IHandle<JobProgressed>, IHandle<ResourcesSampled>
{
    private readonly HashSet<SessionId> rested = [];
    private readonly List<(JobId Job, SessionId Session, ProcessTreeId Tree)> kept = [];

    public ValueTask HandleAsync(JobSessionRested integrationEvent, CancellationToken cancellationToken)
    {
        rested.Add(integrationEvent.Session);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var facts = new SessionFacts(
            integrationEvent.Session,
            Folders.Key(integrationEvent.WorkingDirectory),
            integrationEvent.Provider.Id,
            integrationEvent.Connection)
        {
            Tree = integrationEvent.ProcessTree,
        };
        book.Attribute(attribution => attribution.Opened(facts));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Attribute(attribution => attribution.Tied(integrationEvent.Session, integrationEvent.Job));

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) =>
        await EndedAsync(integrationEvent.Session, cancellationToken);

    public async ValueTask HandleAsync(SessionStopped integrationEvent, CancellationToken cancellationToken) =>
        await EndedAsync(integrationEvent.Session, cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (!integrationEvent.Status.Ended())
        {
            await housekeeper.ReleaseAsync(integrationEvent.Job, cancellationToken);
            return;
        }

        foreach (var left in kept.Where(found => found.Job == integrationEvent.Job).ToList())
        {
            kept.Remove(left);
            await reaper.ReapEndedAsync(left.Tree, left.Session, left.Job, cancellationToken);
        }

        var worktrees = book.Attribution.HomesOf(integrationEvent.Job);

        foreach (var worktree in worktrees)
        {
            await leases.ReleaseWorktreeAsync(worktree, cancellationToken);
        }

        await housekeeper.RetainAsync(integrationEvent.Job, integrationEvent.Status, worktrees, reaper, cancellationToken);
    }

    public async ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken) =>
        await housekeeper.SweepAsync(integrationEvent.Sample, reaper, cancellationToken);

    private async Task EndedAsync(SessionId session, CancellationToken cancellationToken)
    {
        var attribution = book.Attribution;

        if (rested.Remove(session))
        {
            foreach (var (tree, job) in attribution.Of(session).Bind(facts => facts.Tree).Bind(tree => attribution.JobOf(session).Map(job => (tree, job))).Match<(ProcessTreeId, JobId)[]>(found => [found], () => []))
            {
                if (!await reaper.CloseIfEmptyAsync(tree, cancellationToken))
                {
                    kept.Add((job, session, tree));
                }
            }

            return;
        }

        await attribution.Of(session)
            .Bind(facts => facts.Tree)
            .Match(tree => reaper.ReapEndedAsync(tree, session, attribution.JobOf(session), cancellationToken), () => Task.CompletedTask);
    }
}
