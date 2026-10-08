namespace Avala.Agents.Contracts.Events;

public readonly record struct TokenUsage(long Input, long Output, long CacheRead, long CacheWrite, long Reasoning);

public readonly record struct Cost(decimal Amount, string Currency);

public readonly record struct UsageLimit(string Window, double UsedFraction, DateTimeOffset? ResetsAt);

public readonly record struct PlanStep(string Title, PlanStepStatus Status);
