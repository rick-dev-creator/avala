using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Contracts;

public interface IJobs
{
    ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken);

    ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken);
}

public sealed record JobRequest(string RepositoryPath, string Instruction, int AttemptsPerRound = 3)
{
    public Option<Autonomy> Autonomy { get; init; }

    public Option<ConnectionName> Connection { get; init; }
}

public enum ContinuedIn
{
    SameSession,
    ResumedConversation,
    NewConversation,
}

public sealed record JobContinuation(JobId Job, SessionId Session, ContinuedIn Conversation);

public enum JobRejection
{
    EmptyRepository,
    EmptyInstruction,
    InvalidAttemptBudget,
    InvalidRequest,
    UnknownJob,
    NotRunning,
    NotHeld,
    EmptyMessage,
    WorkspaceUnavailable,
    AgentUnavailable,
    UnknownConnection,
    UnusableConnection,
}
