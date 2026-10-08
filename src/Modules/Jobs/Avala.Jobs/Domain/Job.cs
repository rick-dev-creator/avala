using Avala.Sdk;
using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Jobs.Domain;

internal sealed class Job : IAggregateRoot<JobId>
{
    private readonly List<Attempt> attempts = [];
    private readonly StateMachine<JobState, JobTrigger> machine;

    private Job(JobId id, Instruction instruction, AttemptBudget budget)
    {
        Id = id;
        Instruction = instruction;
        Budget = budget;
        machine = JobLifecycle.Create(() => State, state => State = state, HasRetriesLeft);
    }

    public JobId Id { get; }

    public Instruction Instruction { get; }

    public AttemptBudget Budget { get; }

    public JobState State { get; private set; } = JobState.Draft;

    public IReadOnlyList<Attempt> Attempts => attempts;

    public static Result<Job, JobError> Create(JobId id, Instruction instruction, AttemptBudget budget) =>
        new Job(id, instruction, budget);

    public Result<JobSubmitted, JobError> Submit() =>
        machine.TryFire(JobTrigger.Submit, JobError.CannotSubmit)
            .Map(_ => new JobSubmitted(Id));

    public Result<AttemptStarted, JobError> Start() =>
        machine.TryFire(JobTrigger.Start, JobError.CannotStart)
            .Map(_ => Begin(AttemptOrigin.Initial, guidance: null));

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
            .Map(_ => Interrupt(new JobDiscarded(Id, from)));
    }

    public Result<JobFailed, JobError> Fail(FailureReason reason) =>
        machine.TryFire(JobTrigger.Fail, JobError.CannotFail)
            .Map(_ => Interrupt(new JobFailed(Id, reason)));

    private bool HasRetriesLeft() =>
        attempts.Count - attempts.FindLastIndex(attempt => attempt.Origin != AttemptOrigin.Retry) < Budget.AttemptsPerRound;

    private AttemptStarted Begin(AttemptOrigin origin, Feedback? guidance)
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

    private TEvent Interrupt<TEvent>(TEvent domainEvent)
    {
        if (attempts is [.., { IsUnderway: true } current])
        {
            current.Conclude(AttemptOutcome.Interrupted);
        }

        return domainEvent;
    }
}
