using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Governance;

internal sealed class SessionGovernor(GovernanceBook book, IPolicyFiles files, PermissionResponder responder, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var read = await files.ReadAsync(integrationEvent.WorkingDirectory, cancellationToken);
        var (policy, file, error) = read.Resolved;

        var report = new SessionPolicy(integrationEvent.Session, file, error, policy.Rules, read.Origin)
        {
            Autonomy = policy.Declared,
            Strategy = policy.Strategy,
        };
        book.Keep(book.Of(integrationEvent.Session).OpenedIn(integrationEvent.WorkingDirectory, policy, report));

        await bus.PublishAsync(new PolicyLoaded(report), cancellationToken);
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        var governed = book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job, integrationEvent.Autonomy);
        book.Keep(governed);

        await governed.Autonomy.Match(
            applied => bus.PublishAsync(new AutonomyApplied(applied), cancellationToken).AsTask(),
            () => Task.CompletedTask);
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case PermissionRequested requested:
                var decision = await responder.DecideAsync(book.Of(requested.Session), requested, book.SessionRulesOf(requested.Session), cancellationToken);
                book.Keep(book.Of(requested.Session).Decided(decision));
                await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
                break;
            case FormRequested asked:
                var form = await responder.DecideAsync(book.Of(asked.Session), asked, cancellationToken);
                book.Keep(book.Of(asked.Session).Asked(form));
                await bus.PublishAsync(new FormDecided(form), cancellationToken);
                break;
        }
    }
}
