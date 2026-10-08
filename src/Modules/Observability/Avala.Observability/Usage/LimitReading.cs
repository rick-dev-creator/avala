using Avala.Agents.Contracts.Events;

namespace Avala.Observability.Usage;

internal readonly record struct LimitReading(UsageLimit Limit, DateTimeOffset At);
