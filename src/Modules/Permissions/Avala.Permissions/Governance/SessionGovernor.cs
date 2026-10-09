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
        var (policy, file, error) = read.Rules.Match(
            found => found.Match(
                rules => (PermissionPolicy.With(rules), PolicyFileStatus.Applied, Option<PolicyError>.None),
                () => (PermissionPolicy.BuiltIn, PolicyFileStatus.Absent, Option<PolicyError>.None)),
            rejection => (PermissionPolicy.BuiltIn, PolicyFileStatus.Rejected, Option<PolicyError>.Some(rejection)));

        var report = new SessionPolicy(integrationEvent.Session, file, error, policy.Rules, read.Origin);
        book.Keep(book.Of(integrationEvent.Session).OpenedIn(integrationEvent.WorkingDirectory, policy, report));

        await bus.PublishAsync(new PolicyLoaded(report), cancellationToken);
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Keep(book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job));

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not PermissionRequested requested)
        {
            return;
        }

        var decision = await responder.DecideAsync(book.Of(requested.Session), requested, cancellationToken);

        book.Keep(book.Of(requested.Session).Decided(decision));
        await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
    }
}
