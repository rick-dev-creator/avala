using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Avala.Workspaces.Contracts;
using Stateless;

namespace Avala.Jobs.Jobs;

internal sealed class Job : IAggregateRoot<JobId>
{
    private readonly List<Attempt> attempts = [];
    private readonly StateMachine<JobState, JobTrigger> machine;

    private Job(JobId id, Instruction instruction, AttemptBudget budget, RepositoryPath repository)
    {
        Id = id;
        Instruction = instruction;
        Budget = budget;
        Repository = repository;
        machine = JobLifecycle.Create(() => State, state => State = state, HasRetriesLeft);
    }

    public JobId Id { get; }

    public Instruction Instruction { get; }

    public AttemptBudget Budget { get; }

    public RepositoryPath Repository { get; }

    public JobState State { get; private set; } = JobState.Draft;

    public Option<WorkspaceId> Workspace { get; private set; }

    public Option<SessionId> Session { get; private set; }

    public IReadOnlyList<Attempt> Attempts => attempts;

    public static Result<Job, JobError> Create(JobId id, Instruction instruction, AttemptBudget budget, RepositoryPath repository) =>
        new Job(id, instruction, budget, repository);

    public Result<JobSubmitted, JobError> Submit() =>
        machine.TryFire(JobTrigger.Submit, JobError.CannotSubmit)
            .Map(_ => new JobSubmitted(Id));

    public Result<AttemptStarted, JobError> Start(WorkspaceId workspace, SessionId session) =>
        machine.TryFire(JobTrigger.Start, JobError.CannotStart)
            .Map(_ =>
            {
                Workspace = workspace;
                Session = session;

                return Begin(AttemptOrigin.Initial, Option<Feedback>.None);
            });

    public Result<AttemptStarted, JobError> Recover(SessionId session) =>
        machine.TryFire(JobTrigger.Recover, JobError.CannotRecover)
            .Map(_ =>
            {
                Session = session;
                InterruptUnderwayAttempt();

                return Begin(AttemptOrigin.Recovery, Option<Feedback>.None);
            });

    public Result<AttemptCompleted, JobError> CompleteTurn() =>
        machine.TryFire(JobTrigger.CompleteTurn, JobError.CannotCompleteTurn)
            .Map(_ => new AttemptCompleted(Id, Conclude(AttemptOutcome.AwaitingCheck)));

    public Result<AttemptPassed, JobError> Pass() =>
        machine.TryFire(JobTrigger.Pass, JobError.CannotPass)
            .Map(_ => new AttemptPassed(Id, Conclude(AttemptOutcome.Passed)));

    public Result<AttemptRetried, JobError> Retry(Feedback feedback) =>
        machine.TryFire(JobTrigger.Retry, State == JobState.Checking ? JobError.AttemptBudgetExhausted : JobError.CannotRetry)
            .Map(_ => new AttemptRetried(Id, Conclude(AttemptOutcome.Rejected), Begin(AttemptOrigin.Retry, feedback).Attempt, feedback));

    public Result<HelpRequested, JobError> RequestHelp() =>
        machine.TryFire(JobTrigger.RequestHelp, JobError.CannotRequestHelp)
            .Map(_ => new HelpRequested(Id, Conclude(AttemptOutcome.Rejected)));

    public Result<AttemptStarted, JobError> Hint(Feedback guidance) =>
        machine.TryFire(JobTrigger.Hint, JobError.CannotHint)
            .Map(_ => Begin(AttemptOrigin.Hint, guidance));

    public Result<AttemptStarted, JobError> SendBack(Feedback feedback) =>
        machine.TryFire(JobTrigger.SendBack, JobError.CannotSendBack)
            .Map(_ => Begin(AttemptOrigin.SendBack, feedback));

    public Result<JobApproved, JobError> Approve() =>
        machine.TryFire(JobTrigger.Approve, JobError.CannotApprove)
            .Map(_ => new JobApproved(Id));

    public Result<JobDiscarded, JobError> Discard()
    {
        var from = State;

        return machine.TryFire(JobTrigger.Discard, JobError.CannotDiscard)
            .Map(_ =>
            {
                InterruptUnderwayAttempt();

                return new JobDiscarded(Id, from);
            });
    }

    public Result<JobFailed, JobError> Fail(FailureReason reason) =>
        machine.TryFire(JobTrigger.Fail, JobError.CannotFail)
            .Map(_ =>
            {
                InterruptUnderwayAttempt();

                return new JobFailed(Id, reason);
            });

    private bool HasRetriesLeft() =>
        attempts.Count - attempts.FindLastIndex(attempt => attempt.Origin != AttemptOrigin.Retry) < Budget.AttemptsPerRound;

    private AttemptStarted Begin(AttemptOrigin origin, Option<Feedback> guidance)
    {
        var number = attempts.Count == 0 ? AttemptNumber.First : attempts[^1].Number.Next;
        attempts.Add(new Attempt(number, origin, guidance));

        return new AttemptStarted(Id, number, origin, guidance);
    }

    private AttemptNumber Conclude(AttemptOutcome outcome)
    {
        var current = attempts[^1];
        current.Conclude(outcome);

        return current.Number;
    }

    private void InterruptUnderwayAttempt()
    {
        if (attempts is [.., { IsUnderway: true } current])
        {
            current.Conclude(AttemptOutcome.Interrupted);
        }
    }
}
