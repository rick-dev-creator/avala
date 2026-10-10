using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Governance;

internal sealed class SessionGovernor(GovernanceBook book, IPolicyFiles files, PermissionResponder responder, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<JobProgressed>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            await book.EndedAsync(new EndedJob(integrationEvent.Job, integrationEvent.Status), cancellationToken);
        }
    }

    public async ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var read = await files.ReadAsync(integrationEvent.WorkingDirectory, cancellationToken);
        var (policy, file, error) = read.Resolved;

        var report = new SessionPolicy(integrationEvent.Session, file, error, policy.Rules, read.Origin)
        {
            Autonomy = policy.Declared,
            Strategy = policy.Strategy,
        };
        await book.OpenedAsync(book.Of(integrationEvent.Session).OpenedIn(integrationEvent.WorkingDirectory, policy, report), report, cancellationToken);

        await bus.PublishAsync(new PolicyLoaded(report), cancellationToken);
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        var governed = book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job, integrationEvent.Autonomy);
        await book.WorkingOnAsync(governed, cancellationToken);

        await governed.Autonomy.Match(
            applied => bus.PublishAsync(new AutonomyApplied(applied), cancellationToken).AsTask(),
            () => Task.CompletedTask);
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case PermissionRequested requested:
                var governed = book.Of(requested.Session);
                var decision = await responder.DecideAsync(governed, requested, book.RulesOf(governed), cancellationToken);
                await book.DecidedAsync(decision, cancellationToken);
                await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
                break;
            case FormRequested asked:
                var form = await responder.DecideAsync(book.Of(asked.Session), asked, cancellationToken);
                await book.AskedAsync(form, cancellationToken);
                await bus.PublishAsync(new FormDecided(form), cancellationToken);
                break;
            case RequestWithdrawn withdrawn:
                await WithdrawAsync(book.Of(withdrawn.Session), withdrawn.Item, cancellationToken);
                break;
        }
    }

    private async Task WithdrawAsync(GovernedSession session, ItemId item, CancellationToken cancellationToken)
    {
        foreach (var waiting in session.WaitingPermission(item).Match<PolicyDecision[]>(found => [found with { Delivery = DecisionDelivery.Withdrawn }], () => []))
        {
            await book.WithdrawnAsync(waiting, cancellationToken);
            await bus.PublishAsync(new PermissionDecided(waiting), cancellationToken);
        }

        foreach (var waiting in session.WaitingForm(item).Match<FormDecision[]>(found => [found with { Delivery = DecisionDelivery.Withdrawn }], () => []))
        {
            await book.WithdrawnAsync(waiting, cancellationToken);
            await bus.PublishAsync(new FormDecided(waiting), cancellationToken);
        }
    }
}
