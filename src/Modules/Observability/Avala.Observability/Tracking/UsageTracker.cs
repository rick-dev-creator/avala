using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Usage;
using Avala.Sdk.Events;

namespace Avala.Observability.Tracking;

internal sealed class UsageTracker(UsageBook book, IUsageMetrics metrics, TimeProvider clock, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken) =>
        await book.AttributeAsync(
            book.Of(integrationEvent.Session).OpenedBy(integrationEvent.Provider, integrationEvent.Account, integrationEvent.Connection, clock.GetUtcNow()),
            cancellationToken);

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken) =>
        await book.AttributeAsync(book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job), cancellationToken);

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        var before = book.Of(integrationEvent.Event.Session);
        var after = before.Apply(integrationEvent.Event, at);
        var source = new UsageSource(before.Provider, before.Connection);

        switch (integrationEvent.Event)
        {
            case UsageReported usage:
                await book.RecordAsync(after, UsageFact.Of(usage, at), cancellationToken);
                metrics.RecordUsage(source, usage.Tokens, usage.Cost);
                await bus.PublishAsync(new UsageRecorded(after.Session, after.Job), cancellationToken);
                break;
            case LimitReported limit:
                await book.RecordAsync(after, UsageFact.Of(limit, at), cancellationToken);
                metrics.RecordLimit(source, limit.Limit);
                await bus.PublishAsync(new UsageRecorded(after.Session, after.Job), cancellationToken);
                break;
            case TurnCompleted completed when after.Turns != before.Turns:
                var duration = before.Elapsed(completed.Turn, at);
                await book.RecordAsync(after, UsageFact.Turn(after.Session, completed.Outcome, duration.Match(elapsed => elapsed, () => TimeSpan.Zero), at), cancellationToken);
                metrics.RecordTurn(source, completed.Outcome, duration);
                break;
            default:
                book.Keep(after);
                break;
        }
    }
}
