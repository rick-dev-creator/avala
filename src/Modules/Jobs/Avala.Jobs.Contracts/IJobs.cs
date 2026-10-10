using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Contracts;

public interface IJobs
{
    ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken);

    ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken);

    ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken);

    ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken);
}

public interface IOpenDeliveries
{
    ValueTask<Result<JobApproval, JobRejection>> ApproveThroughAsync(JobId job, string strategy, CancellationToken cancellationToken);

    ValueTask<Result<JobContinuation, JobRejection>> ReopenAsync(JobId job, string feedback, CancellationToken cancellationToken);
}

public interface IJobAdmission
{
    ValueTask AdmitAsync(JobId job, CancellationToken cancellationToken);
}

public interface IRecoveryDeferral
{
    ValueTask<bool> DefersAsync(JobId job, CancellationToken cancellationToken);
}

public interface IJobBriefing
{
    ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken);
}

public sealed record JobRequest(string RepositoryPath, string Instruction, int AttemptsPerRound = 3)
{
    public Option<Autonomy> Autonomy { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public Option<JobId> Parent { get; init; }

    public Option<ConnectionChoice> Choice { get; init; }

    public ModelChoice Model { get; init; } = ModelChoice.Default;
}

public enum ContinuedIn
{
    SameSession,
    ResumedConversation,
    NewConversation,
}

public sealed record JobContinuation(JobId Job, SessionId Session, ContinuedIn Conversation);

public sealed record JobSteered(JobId Job, SessionId Session, TurnId Turn);

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
    NotDiscardable,
    NotAwaitingReview,
    InvalidJobFile,
    UnknownApprovalStrategy,
    NoBaseBranch,
    MergeConflict,
    BaseCheckoutDirty,
    BaseMoved,
    DeliveryFailed,
    UnknownParent,
    ParentNotRunning,
    SameConnection,
    NotDeferred,
    NotResumable,
    NotSteerable,
    UnofferedModel,
    UnofferedEffort,
    NotApproved,
}
