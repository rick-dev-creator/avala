using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Observability.Tracking;

internal sealed class UsageTracker(UsageBook book, IUsageMetrics metrics, TimeProvider clock)
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

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        var before = book.Of(integrationEvent.Event.Session);
        var after = before.Apply(integrationEvent.Event, at);
        book.Keep(after);

        switch (integrationEvent.Event)
        {
            case UsageReported usage:
                metrics.RecordUsage(before.Provider, usage.Tokens, usage.Cost);
                break;
            case LimitReported limit:
                metrics.RecordLimit(before.Provider, limit.Limit);
                break;
            case TurnCompleted completed when after.Turns != before.Turns:
                metrics.RecordTurn(before.Provider, completed.Outcome, before.Elapsed(completed.Turn, at));
                break;
        }

        return ValueTask.CompletedTask;
    }
}
