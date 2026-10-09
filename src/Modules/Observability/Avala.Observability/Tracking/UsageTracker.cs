using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk.Events;

namespace Avala.Observability.Tracking;

internal sealed class UsageTracker(UsageBook book, IUsageMetrics metrics, TimeProvider clock, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        book.Keep(book.Of(integrationEvent.Session).OpenedBy(integrationEvent.Provider));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Keep(book.Of(integrationEvent.Session).WorkingOn(integrationEvent.Job));

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        var before = book.Of(integrationEvent.Event.Session);
        var after = before.Apply(integrationEvent.Event, at);
        book.Keep(after);

        switch (integrationEvent.Event)
        {
            case UsageReported usage:
                metrics.RecordUsage(before.Provider, usage.Tokens, usage.Cost);
                await bus.PublishAsync(new UsageRecorded(after.Session, after.Job), cancellationToken);
                break;
            case LimitReported limit:
                metrics.RecordLimit(before.Provider, limit.Limit);
                await bus.PublishAsync(new UsageRecorded(after.Session, after.Job), cancellationToken);
                break;
            case TurnCompleted completed when after.Turns != before.Turns:
                metrics.RecordTurn(before.Provider, completed.Outcome, before.Elapsed(completed.Turn, at));
                break;
        }
    }
}
