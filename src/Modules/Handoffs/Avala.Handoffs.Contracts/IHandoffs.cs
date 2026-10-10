using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Contracts;

public interface IHandoffs
{
    IReadOnlyList<HandoffRecord> OfJob(JobId job);

    Option<ResetWait> WaitOf(JobId job);
}

public enum OnLimit
{
    Hold,
    HandOffSameHarness,
    HandOffAnyHarness,
}

public enum HandoffError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    UnknownAction,
    InvalidThreshold,
    InvalidConnections,
}

public sealed record LimitReason(ConnectionName Connection, string Window, double Used, double Threshold);

public sealed record HandoffRecord(JobId Job, int Attempt, ConnectionName From, ConnectionName To, Option<LimitReason> Why, DateTimeOffset At)
{
    public IReadOnlyList<Cost> Spent { get; init; } = [];

    public long Tokens { get; init; }
}

public sealed record ResetWait(JobId Job, ConnectionName Connection, string Window, Option<DateTimeOffset> ResumesAt, DateTimeOffset Since);

public sealed record HandoffRecorded(HandoffRecord Handoff) : IIntegrationEvent;

public sealed record JobWaitsForReset(ResetWait Wait) : IIntegrationEvent;

public sealed record JobResumedAtReset(JobId Job, ConnectionName Connection, ContinuedIn Conversation, DateTimeOffset At) : IIntegrationEvent;
