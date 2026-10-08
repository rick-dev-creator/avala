using Avala.Agents.Contracts.Sessions;
using Avala.Sdk.Events;

namespace Avala.Jobs.Contracts;

public enum JobStatus
{
    Draft,
    Preparing,
    Running,
    Checking,
    NeedsHelp,
    AwaitingReview,
    Approved,
    Discarded,
    Failed,
}

public sealed record JobSubmitted(JobId Job) : IIntegrationEvent;

public sealed record JobProgressed(JobId Job, JobStatus Status) : IIntegrationEvent;

public sealed record JobSessionStarted(JobId Job, SessionId Session) : IIntegrationEvent;
