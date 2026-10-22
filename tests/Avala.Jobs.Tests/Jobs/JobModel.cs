using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using JobId = Avala.Jobs.Contracts.JobId;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Avala.Workspaces.Contracts;
using AttemptOrigin = Avala.Jobs.Contracts.AttemptOrigin;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;
using HoldReason = Avala.Jobs.Contracts.HoldReason;
using JobFailure = Avala.Jobs.Contracts.JobFailure;

namespace Avala.Jobs.Tests.Jobs;

internal enum Command
{
    Submit,
    Start,
    Recover,
    Recheck,
    CompleteTurn,
    Pass,
    Retry,
    RequestHelp,
    Hint,
    HandOff,
    SendBack,
    Wake,
    ContinueFromDevice,
    Approve,
    Discard,
    Fail,
    Hold,
    CouldNotStart,
    RetryLaunch,
    Reopen,
    ChooseModel,
}

internal sealed record Step(Command Command, HoldReason Reason, ConnectionName Connection)
{
    public override string ToString() => Command switch
    {
        Command.Hold => $"Hold({Reason})",
        Command.HandOff => $"HandOff({Connection.Value})",
        _ => Command.ToString(),
    };
}

internal sealed record Expected(JobError? Error, IDomainEvent? Event);

internal sealed class JobModel(JobId id, int attemptsPerRound)
{
    public const string Problem = "The file is locked.";

    private static readonly JobState[] Open = [JobState.Draft, JobState.Preparing, JobState.Running, JobState.Checking, JobState.NeedsHelp, JobState.AwaitingReview];

    private readonly List<(AttemptOrigin Origin, AttemptOutcome Outcome)> attempts = [];

    public JobState State { get; private set; } = JobState.Draft;

    public HoldReason? Held { get; private set; }

    public JobFailure? Failure { get; private set; }

    public bool Ended { get; private set; }

    public ConnectionName? Connection { get; private set; }

    private Option<WorkspaceId> workspace;

    public IReadOnlyList<(AttemptOrigin Origin, AttemptOutcome Outcome)> Attempts => attempts;

    public bool Accepts(Step step)
    {
        var copy = new JobModel(id, attemptsPerRound) { State = State, Held = Held, Failure = Failure, Ended = Ended, Connection = Connection, workspace = workspace };
        copy.attempts.AddRange(attempts);

        return copy.Apply(step).Error is null;
    }

    private bool Started => Held != HoldReason.CouldNotStart;

    private AttemptNumber Last => new(attempts.Count);

    public Expected Apply(Step step) => step.Command switch
    {
        Command.Submit => When(State == JobState.Draft, JobError.CannotSubmit, () => Move(JobState.Preparing, new JobSubmitted(id))),
        Command.Start => When(State == JobState.Preparing, JobError.CannotStart, () =>
        {
            Connection = Given.Connection;
            workspace = Given.Workspace;
            return Move(JobState.Running, Begin(AttemptOrigin.Initial, Option<Feedback>.None));
        }),
        Command.Recover => When(State == JobState.Running, JobError.CannotRecover, () =>
        {
            Interrupt();
            return Move(JobState.Running, Begin(AttemptOrigin.Recovery, Option<Feedback>.None));
        }),
        Command.Recheck => When(State == JobState.Checking, JobError.CannotRecheck, () => Move(JobState.Checking, new ChecksResumed(id, Last))),
        Command.CompleteTurn => When(State == JobState.Running, JobError.CannotCompleteTurn, () => Move(JobState.Checking, new AttemptCompleted(id, Conclude(AttemptOutcome.AwaitingCheck)))),
        Command.Pass => When(State == JobState.Checking, JobError.CannotPass, () => Move(JobState.AwaitingReview, new AttemptPassed(id, Conclude(AttemptOutcome.Passed)))),
        Command.Retry => State != JobState.Checking ? new Expected(JobError.CannotRetry, null)
            : !RetriesLeft ? new Expected(JobError.AttemptBudgetExhausted, null)
            : Move(JobState.Running, Retried()),
        Command.RequestHelp => When(State == JobState.Checking && !RetriesLeft, JobError.CannotRequestHelp, () => Move(JobState.NeedsHelp, new HelpRequested(id, Conclude(AttemptOutcome.Rejected)))),
        Command.Hint => When(State == JobState.NeedsHelp && Started, JobError.CannotHint, () => Move(JobState.Running, Begin(AttemptOrigin.Hint, Given.Feedback))),
        Command.HandOff => HandOff(step.Connection),
        Command.SendBack => SentBack(AttemptOrigin.SendBack),
        Command.Wake => SentBack(AttemptOrigin.Woken),
        Command.ContinueFromDevice => SentBack(AttemptOrigin.Device),
        Command.Approve => When(State == JobState.AwaitingReview, JobError.CannotApprove, () =>
        {
            Ended = true;
            return Move(JobState.Approved, new JobApproved(id));
        }),
        Command.Discard => When(Open.Contains(State), JobError.CannotDiscard, () =>
        {
            var from = State;
            Interrupt();
            Ended = true;
            return Move(JobState.Discarded, new JobDiscarded(id, from));
        }),
        Command.Fail => When(State is JobState.Preparing or JobState.Running or JobState.Checking, JobError.CannotFail, () =>
        {
            Interrupt();
            Ended = true;
            Failure = JobFailure.AgentFailed;
            return Move(JobState.Failed, new JobFailed(id, JobFailure.AgentFailed));
        }),
        Command.Hold => When(Holds(step.Reason), JobError.CannotHold, () =>
        {
            Interrupt();
            Held = step.Reason;
            return Move(JobState.NeedsHelp, new JobHeld(id, Last, step.Reason));
        }),
        Command.CouldNotStart => When(State is JobState.Preparing or JobState.Running, JobError.CannotHold, () =>
        {
            Interrupt();
            Held = HoldReason.CouldNotStart;
            return Move(JobState.NeedsHelp, new StartFailed(id, attempts.Count == 0 ? Option<AttemptNumber>.None : Last, Problem));
        }),
        Command.RetryLaunch => When(State == JobState.NeedsHelp && !Started, JobError.CannotRetryLaunch, () =>
        {
            Held = null;
            return Move(JobState.Preparing, new LaunchRetried(id, workspace));
        }),
        Command.Reopen => When(State == JobState.Approved, JobError.CannotReopen, () =>
        {
            Ended = false;
            return Move(JobState.Running, Begin(AttemptOrigin.Reopen, Given.Feedback));
        }),
        Command.ChooseModel => When(State is JobState.NeedsHelp or JobState.AwaitingReview, JobError.CannotChooseModel, () => new Expected(null, new ModelChosen(id, Given.Model))),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private bool RetriesLeft
    {
        get
        {
            var start = attempts.Count - 1;

            while (start > 0 && (attempts[start].Origin == AttemptOrigin.Retry || (attempts[start].Origin == AttemptOrigin.Handoff && attempts[start - 1].Outcome == AttemptOutcome.Rejected)))
            {
                start--;
            }

            return attempts.Count - start < attemptsPerRound;
        }
    }

    private bool Holds(HoldReason reason) => reason switch
    {
        HoldReason.CouldNotStart => false,
        HoldReason.BudgetExceeded => State is JobState.Running or JobState.Checking or JobState.AwaitingReview,
        HoldReason.InvalidChecks => State == JobState.Checking,
        _ => State == JobState.Running,
    };

    private Expected HandOff(ConnectionName connection)
    {
        if (Connection == connection)
        {
            return new Expected(JobError.SameConnection, null);
        }

        var checking = State == JobState.Checking && RetriesLeft;

        if (!checking && !(State == JobState.NeedsHelp && Started))
        {
            return new Expected(JobError.CannotHandOff, null);
        }

        if (checking)
        {
            _ = Conclude(AttemptOutcome.Rejected);
        }

        Connection = connection;

        return Move(JobState.Running, Begin(AttemptOrigin.Handoff, Given.Feedback));
    }

    private Expected SentBack(AttemptOrigin origin) =>
        When(State == JobState.AwaitingReview, JobError.CannotSendBack, () => Move(JobState.Running, Begin(origin, Given.Feedback)));

    private AttemptRetried Retried()
    {
        var rejected = Conclude(AttemptOutcome.Rejected);

        return new AttemptRetried(id, rejected, Begin(AttemptOrigin.Retry, Given.Feedback).Attempt, Given.Feedback);
    }

    private static Expected When(bool allowed, JobError rejection, Func<Expected> transition) =>
        allowed ? transition() : new Expected(rejection, null);

    private Expected Move(JobState state, IDomainEvent? happened)
    {
        State = state;

        return new Expected(null, happened);
    }

    private AttemptStarted Begin(AttemptOrigin origin, Option<Feedback> guidance)
    {
        Held = null;
        attempts.Add((origin, AttemptOutcome.Running));

        return new AttemptStarted(id, Last, origin, guidance);
    }

    private AttemptNumber Conclude(AttemptOutcome outcome)
    {
        attempts[^1] = (attempts[^1].Origin, outcome);

        return Last;
    }

    private void Interrupt()
    {
        if (attempts is [.., (_, AttemptOutcome.Running or AttemptOutcome.AwaitingCheck)])
        {
            _ = Conclude(AttemptOutcome.Interrupted);
        }
    }
}
