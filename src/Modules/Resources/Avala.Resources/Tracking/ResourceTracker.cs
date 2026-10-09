using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Usage;
using Avala.Sdk.Events;

namespace Avala.Resources.Tracking;

internal sealed class ResourceTracker(ResourceBook book, OrphanReaper reaper, PortLeases leases, WorktreeHousekeeper housekeeper)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<SessionEnded>, IHandle<SessionStopped>, IHandle<JobProgressed>, IHandle<ResourcesSampled>
{
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
            return;
        }

        var worktrees = book.Attribution.HomesOf(integrationEvent.Job);

        foreach (var worktree in worktrees)
        {
            await leases.ReleaseAsync(worktree, cancellationToken);
        }

        await housekeeper.RetainAsync(integrationEvent.Job, integrationEvent.Status, worktrees, cancellationToken);
    }

    public async ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken) =>
        await housekeeper.SweepAsync(cancellationToken);

    private async Task EndedAsync(SessionId session, CancellationToken cancellationToken)
    {
        var attribution = book.Attribution;

        await attribution.Of(session)
            .Bind(facts => facts.Tree)
            .Match(tree => reaper.ReapEndedAsync(tree, session, attribution.JobOf(session), cancellationToken), () => Task.CompletedTask);
    }
}
