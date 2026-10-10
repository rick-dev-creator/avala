using Avala.Agents.Contracts.Connections;
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

    public DateTimeOffset Submitted { get; private init; }

    public Option<DateTimeOffset> Ended { get; private set; }

    public JobState State { get; private set; } = JobState.Draft;

    public Option<WorkspaceId> Workspace { get; private set; }

    public Option<SessionId> Session { get; private set; }

    public Option<ResumeToken> Resume { get; private set; }

    public Option<Autonomy> Autonomy { get; private set; }

    public Option<ConnectionName> Connection { get; private set; }

    public Option<JobId> Parent { get; private init; }

    public ModelChoice Model { get; private init; } = ModelChoice.Default;

    public IReadOnlyList<Attempt> Attempts => attempts;

    public static Result<Job, JobError> Create(
        JobId id,
        Instruction instruction,
        AttemptBudget budget,
        RepositoryPath repository,
        DateTimeOffset submitted,
        Option<Autonomy> autonomy = default,
        Option<ConnectionName> connection = default,
        Option<JobId> parent = default,
        Option<ModelChoice> model = default) =>
        new Job(id, instruction, budget, repository)
        {
            Submitted = submitted,
            Autonomy = autonomy,
            Connection = connection,
            Parent = parent,
            Model = model.Match(chosen => chosen, () => ModelChoice.Default),
        };

    public Result<JobSubmitted, JobError> Submit() =>
        machine.TryFire(JobTrigger.Submit, JobError.CannotSubmit)
            .Map(_ => new JobSubmitted(Id));

    public Result<AttemptStarted, JobError> Start(WorkspaceId workspace, SessionId session, ConnectionName connection) =>
        machine.TryFire(JobTrigger.Start, JobError.CannotStart)
            .Map(_ =>
            {
                Workspace = workspace;
                Session = session;
                Connection = connection;

                return Begin(AttemptOrigin.Initial, Option<Feedback>.None);
            });

    public Result<AttemptStarted, JobError> Recover(SessionId session, bool resumed) =>
        machine.TryFire(JobTrigger.Recover, JobError.CannotRecover)
            .Map(_ =>
            {
                Join(session, resumed);
                InterruptUnderwayAttempt();

                return Begin(AttemptOrigin.Recovery, Option<Feedback>.None);
            });

    public Result<ChecksResumed, JobError> Recheck() =>
        machine.TryFire(JobTrigger.Recheck, JobError.CannotRecheck)
            .Map(_ => new ChecksResumed(Id, attempts[^1].Number));

    public Result<ResumeRecorded, JobError> RecordResume(SessionId session, ResumeToken token)
    {
        if (Session != Option<SessionId>.Some(session))
        {
            return JobError.ForeignSession;
        }

        Resume = token;

        return new ResumeRecorded(Id, session);
    }

    public Result<AttemptCompleted, JobError> CompleteTurn() =>
        machine.TryFire(JobTrigger.CompleteTurn, JobError.CannotCompleteTurn)
            .Map(_ => new AttemptCompleted(Id, Conclude(AttemptOutcome.AwaitingCheck)));

    public Result<AttemptPassed, JobError> Pass() =>
        machine.TryFire(JobTrigger.Pass, JobError.CannotPass)
            .Map(_ => new AttemptPassed(Id, Conclude(AttemptOutcome.Passed)));

    public Result<AttemptRetried, JobError> Retry(Feedback feedback) =>
        machine.TryFire(JobTrigger.Retry, State == JobState.Checking ? JobError.AttemptBudgetExhausted : JobError.CannotRetry)
            .Map(_ => new AttemptRetried(Id, Conclude(AttemptOutcome.Rejected), Begin(AttemptOrigin.Retry, feedback).Attempt, feedback));

    public Result<AttemptRetried, JobError> Retry(Feedback feedback, SessionId session, bool resumed) =>
        machine.TryFire(JobTrigger.Retry, State == JobState.Checking ? JobError.AttemptBudgetExhausted : JobError.CannotRetry)
            .Map(_ =>
            {
                var rejected = Conclude(AttemptOutcome.Rejected);
                Join(session, resumed);

                return new AttemptRetried(Id, rejected, Begin(AttemptOrigin.Retry, feedback).Attempt, feedback);
            });

    public Result<HelpRequested, JobError> RequestHelp() =>
        machine.TryFire(JobTrigger.RequestHelp, JobError.CannotRequestHelp)
            .Map(_ => new HelpRequested(Id, Conclude(AttemptOutcome.Rejected)));

    public Result<AttemptStarted, JobError> Hint(Feedback guidance) =>
        machine.TryFire(JobTrigger.Hint, JobError.CannotHint)
            .Map(_ => Begin(AttemptOrigin.Hint, guidance));

    public Result<AttemptStarted, JobError> Hint(Feedback guidance, SessionId session, bool resumed) =>
        machine.TryFire(JobTrigger.Hint, JobError.CannotHint)
            .Map(_ =>
            {
                Join(session, resumed);

                return Begin(AttemptOrigin.Hint, guidance);
            });

    public Result<AttemptStarted, JobError> ContinueOn(Feedback guidance, SessionId session, ConnectionName connection) =>
        Connection == Option<ConnectionName>.Some(connection)
            ? JobError.SameConnection
            : machine.TryFire(JobTrigger.Hint, JobError.CannotHint)
                .Map(_ =>
                {
                    Join(session, resumed: false);
                    Connection = connection;

                    return Begin(AttemptOrigin.Hint, guidance);
                });

    public Result<AttemptStarted, JobError> HandOff(Feedback brief, SessionId session, ConnectionName connection)
    {
        if (Connection == Option<ConnectionName>.Some(connection))
        {
            return JobError.SameConnection;
        }

        var checking = State == JobState.Checking;

        return machine.TryFire(JobTrigger.HandOff, JobError.CannotHandOff)
            .Map(_ =>
            {
                if (checking)
                {
                    Conclude(AttemptOutcome.Rejected);
                }

                Join(session, resumed: false);
                Connection = connection;

                return Begin(AttemptOrigin.Handoff, brief);
            });
    }

    public Result<AttemptStarted, JobError> SendBack(Feedback feedback) =>
        machine.TryFire(JobTrigger.SendBack, JobError.CannotSendBack)
            .Map(_ => Begin(AttemptOrigin.SendBack, feedback));

    public Result<AttemptStarted, JobError> SendBack(Feedback feedback, SessionId session, bool resumed) =>
        machine.TryFire(JobTrigger.SendBack, JobError.CannotSendBack)
            .Map(_ =>
            {
                Join(session, resumed);

                return Begin(AttemptOrigin.SendBack, feedback);
            });

    public Result<JobApproved, JobError> Approve(DateTimeOffset at) =>
        machine.TryFire(JobTrigger.Approve, JobError.CannotApprove)
            .Map(_ =>
            {
                Ended = at;

                return new JobApproved(Id);
            });

    public Result<JobDiscarded, JobError> Discard(DateTimeOffset at)
    {
        var from = State;

        return machine.TryFire(JobTrigger.Discard, JobError.CannotDiscard)
            .Map(_ =>
            {
                End(at);

                return new JobDiscarded(Id, from);
            });
    }

    public Result<JobFailed, JobError> Fail(FailureReason reason, DateTimeOffset at) =>
        machine.TryFire(JobTrigger.Fail, JobError.CannotFail)
            .Map(_ =>
            {
                End(at);

                return new JobFailed(Id, reason);
            });

    public Result<JobHeld, JobError> Hold(HoldReason reason) =>
        machine.TryFire(JobTrigger.Hold, JobError.CannotHold)
            .Map(_ =>
            {
                InterruptUnderwayAttempt();

                return new JobHeld(Id, attempts[^1].Number, reason);
            });

    private bool HasRetriesLeft()
    {
        var start = attempts.Count - 1;

        while (start > 0 && ContinuesRound(start))
        {
            start--;
        }

        return attempts.Count - start < Budget.AttemptsPerRound;
    }

    private bool ContinuesRound(int index) =>
        attempts[index].Origin == AttemptOrigin.Retry
        || (attempts[index].Origin == AttemptOrigin.Handoff && attempts[index - 1].Outcome == AttemptOutcome.Rejected);

    private void Join(SessionId session, bool resumed)
    {
        Session = session;
        Resume = resumed ? Resume : Option<ResumeToken>.None;
    }

    private AttemptStarted Begin(AttemptOrigin origin, Option<Feedback> guidance)
    {
        var number = attempts.Count == 0 ? AttemptNumber.First : attempts[^1].Number.Next;
        attempts.Add(new Attempt(number, origin, guidance, Session));

        return new AttemptStarted(Id, number, origin, guidance);
    }

    private AttemptNumber Conclude(AttemptOutcome outcome)
    {
        var current = attempts[^1];
        current.Conclude(outcome);

        return current.Number;
    }

    private void End(DateTimeOffset at)
    {
        InterruptUnderwayAttempt();
        Ended = at;
    }

    private void InterruptUnderwayAttempt()
    {
        if (attempts is [.., { IsUnderway: true } current])
        {
            current.Conclude(AttemptOutcome.Interrupted);
        }
    }
}
