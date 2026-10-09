using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Governance;

internal sealed class SessionGovernor(GovernanceBook book, IPolicyFiles files, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>
{
    public async ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var (policy, file, error) = (await files.ReadAsync(integrationEvent.WorkingDirectory, cancellationToken)).Match(
            found => found.Match(
                rules => (PermissionPolicy.With(rules), PolicyFileStatus.Applied, Option<PolicyError>.None),
                () => (PermissionPolicy.BuiltIn, PolicyFileStatus.Absent, Option<PolicyError>.None)),
            rejection => (PermissionPolicy.BuiltIn, PolicyFileStatus.Rejected, Option<PolicyError>.Some(rejection)));

        var report = new SessionPolicy(integrationEvent.Session, file, error, policy.Rules);
        book.Keep(book.Of(integrationEvent.Session).OpenedIn(integrationEvent.WorkingDirectory, policy, report));

        await bus.PublishAsync(new PolicyLoaded(report), cancellationToken);
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Keep(book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job));

        return ValueTask.CompletedTask;
    }
}
