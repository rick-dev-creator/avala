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
        foreach (var found in notice.Match<Notice[]>(found => [found], () => []))
        {
            var (offered, summary) = await AboutAsync(found, cancellationToken);

            if (summary.Match(known => !(known.Parent.IsSome && OwnedByTheParent.Contains(offered.Kind)), () => true))
            {
                await dispatcher.OfferAsync(summary.Match(known => offered.About(JobFacts.Of(known.Repository, known.Instruction)), () => offered), cancellationToken);
            }
        }
    }

    private async Task<(Notice Offered, Option<JobSummary> Summary)> AboutAsync(Notice notice, CancellationToken cancellationToken)
    {
        var summary = await SummaryAsync(notice.Job, cancellationToken);
        var parent = notice.Kind == NotificationKind.Decision
            ? summary.Bind(found => found.Companion ? found.Parent : Option<JobId>.None)
            : Option<JobId>.None;

        return await parent.Match(
            async job => (NoticeRules.OfParent(notice, job), await SummaryAsync(job, cancellationToken)),
            () => Task.FromResult((notice, summary)));
    }

    private async Task<Option<JobSummary>> SummaryAsync(Option<JobId> job, CancellationToken cancellationToken) =>
        await job.Match(
            async found => (await catalog.HistoryAsync(found, cancellationToken)).Map(history => history.Summary),
            () => Task.FromResult(Option<JobSummary>.None));
}
