using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Jobs.Contracts;
using Avala.Sdk.Domain;

namespace Avala.Jobs.Jobs;

internal sealed record JobSubmitted(JobId Job) : IDomainEvent;

internal sealed record AttemptStarted(JobId Job, AttemptNumber Attempt, AttemptOrigin Origin, Option<Feedback> Guidance) : IDomainEvent;

internal sealed record AttemptCompleted(JobId Job, AttemptNumber Attempt) : IDomainEvent;

internal sealed record AttemptPassed(JobId Job, AttemptNumber Attempt) : IDomainEvent;

internal sealed record AttemptRetried(JobId Job, AttemptNumber Rejected, AttemptNumber Next, Feedback Feedback) : IDomainEvent;

internal sealed record HelpRequested(JobId Job, AttemptNumber LastAttempt) : IDomainEvent;

internal sealed record JobApproved(JobId Job) : IDomainEvent;

internal sealed record JobDiscarded(JobId Job, JobState From) : IDomainEvent;

internal sealed record JobFailed(JobId Job, FailureReason Reason) : IDomainEvent;

internal sealed record JobHeld(JobId Job, AttemptNumber Attempt, HoldReason Reason) : IDomainEvent;

internal sealed record ResumeRecorded(JobId Job, SessionId Session) : IDomainEvent;
