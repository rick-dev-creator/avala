using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Notices;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Notifications.Dispatching;

internal sealed class NotificationDesk(Dispatcher dispatcher, IJobCatalog catalog, TimeProvider clock) :
    IHandle<PermissionDecided>,
    IHandle<FormDecided>,
    IHandle<ParentAsked>,
    IHandle<PermissionAnswered>,
    IHandle<JobHeld>,
    IHandle<BudgetIntervened>,
    IHandle<JobProgressed>,
    IHandle<LoopEnded>
{
    private static readonly NotificationKind[] OwnedByTheParent = [NotificationKind.Failed, NotificationKind.Review];

    public async ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken)
    {
        await dispatcher.ResolveAsync(NoticeRules.Resolved(integrationEvent), cancellationToken);
        await OfferAsync(NoticeRules.Of(integrationEvent), cancellationToken);
    }

    public async ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        await dispatcher.ResolveAsync(NoticeRules.Resolved(integrationEvent), cancellationToken);
        await OfferAsync(NoticeRules.Of(integrationEvent), cancellationToken);
    }

    public ValueTask HandleAsync(ParentAsked integrationEvent, CancellationToken cancellationToken) =>
        OfferAsync(NoticeRules.Of(integrationEvent), cancellationToken);

    public async ValueTask HandleAsync(PermissionAnswered integrationEvent, CancellationToken cancellationToken) =>
        await dispatcher.ResolveAsync(NoticeRules.Resolved(integrationEvent), cancellationToken);

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken) =>
        OfferAsync(NoticeRules.Of(integrationEvent, clock.GetUtcNow()), cancellationToken);

    public ValueTask HandleAsync(BudgetIntervened integrationEvent, CancellationToken cancellationToken) =>
        OfferAsync(NoticeRules.Of(integrationEvent), cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        await dispatcher.ResolveAsync(NoticeRules.Resolved(integrationEvent), cancellationToken);
        await OfferAsync(NoticeRules.Of(integrationEvent, clock.GetUtcNow()), cancellationToken);
    }

    public async ValueTask HandleAsync(LoopEnded integrationEvent, CancellationToken cancellationToken) =>
        await dispatcher.OfferAsync(NoticeRules.Of(integrationEvent, clock.GetUtcNow()), cancellationToken);

    private async ValueTask OfferAsync(Option<Notice> notice, CancellationToken cancellationToken)
    {
        foreach (var offered in notice.Match<Notice[]>(found => [found], () => []))
        {
            var summary = await offered.Job.Match(
                async job => (await catalog.HistoryAsync(job, cancellationToken)).Map(history => history.Summary),
                () => Task.FromResult(Option<JobSummary>.None));

            if (summary.Match(found => !(found.Parent.IsSome && OwnedByTheParent.Contains(offered.Kind)), () => true))
            {
                await dispatcher.OfferAsync(summary.Match(found => offered.About(JobFacts.Of(found.Repository, found.Instruction)), () => offered), cancellationToken);
            }
        }
    }
}
