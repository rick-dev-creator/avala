using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Observability.Usage;

internal enum FactKind
{
    Usage,
    Limit,
    Turn,
}

internal sealed record UsageFact(
    SessionId Session,
    DateTimeOffset At,
    FactKind Kind,
    TokenUsage Tokens,
    Option<Cost> Cost,
    Option<UsageLimit> Limit,
    Option<TurnOutcome> Outcome,
    TimeSpan Duration)
{
    public static UsageFact Of(UsageReported usage, DateTimeOffset at) =>
        new(usage.Session, at, FactKind.Usage, usage.Tokens, usage.Cost, Option<UsageLimit>.None, Option<TurnOutcome>.None, TimeSpan.Zero);

    public static UsageFact Of(LimitReported limit, DateTimeOffset at) =>
        new(limit.Session, at, FactKind.Limit, default, Option<Cost>.None, limit.Limit, Option<TurnOutcome>.None, TimeSpan.Zero);

    public static UsageFact Turn(SessionId session, TurnOutcome outcome, TimeSpan duration, DateTimeOffset at) =>
        new(session, at, FactKind.Turn, default, Option<Cost>.None, Option<UsageLimit>.None, outcome, duration);
}
