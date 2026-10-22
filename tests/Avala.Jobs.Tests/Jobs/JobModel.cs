using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Avala.Workspaces.Contracts;
using AttemptOrigin = Avala.Jobs.Contracts.AttemptOrigin;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;
using HoldReason = Avala.Jobs.Contracts.HoldReason;
using JobFailure = Avala.Jobs.Contracts.JobFailure;
using JobId = Avala.Jobs.Contracts.JobId;

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
    ContinueOn,
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
    RecordResume,
    JoinedBy,
}

internal sealed record Step(Command Command, HoldReason Reason, ConnectionName Connection, SessionId Session, bool Resumed, bool InSession, bool Revisit)
{
    public int Voice { get; init; }

    public Feedback Said => Voice switch
    {
        1 => Given.Feedback.By(MessageAuthor.Avala),
        2 => Given.Feedback.CausedBy(MessageAuthor.Avala),
        _ => Given.Feedback,
    };

    public MessageAuthor Cause => Voice == 1 ? MessageAuthor.Avala : MessageAuthor.Person;

    public override string ToString() => Command switch
    {
        Command.Hold => $"Hold({Reason})",
        Command.HandOff or Command.ContinueOn => $"{Command}({Connection.Value})",
        Command.Reopen => $"Reopen({(Revisit ? "hint" : "reopen")}{Joined})",
        Command.Recover or Command.RecordResume => $"{Command}({JobModel.Name(Session)}, resumed {Resumed})",
        Command.JoinedBy => $"JoinedBy({Cause})",
        Command.Retry or Command.Hint or Command.SendBack => $"{Command}({Joined.TrimStart(',', ' ')})",
        _ => Command.ToString(),
    };

    private string Joined => InSession ? $", {JobModel.Name(Session)}, resumed {Resumed}" : string.Empty;
}

internal sealed record Expected(JobError? Error, IDomainEvent? Event);

internal sealed class JobModel(JobId id, int attemptsPerRound)
{
    public const string Problem = "The file is locked.";

    private static readonly JobState[] Open = [JobState.Draft, JobState.Preparing, JobState.Running, JobState.Checking, JobState.NeedsHelp, JobState.AwaitingReview];

    private readonly List<(AttemptOrigin Origin, AttemptOutcome Outcome, bool StartsRound, MessageAuthor Author, MessageAuthor Cause)> attempts = [];

    private Feedback said = Given.Feedback;

    private Option<WorkspaceId> workspace;

    public static ResumeToken Token { get; } = new("conversation-1");

    public static ModelChoice Chosen { get; } = new("opus", "high");

    public static IReadOnlyList<SessionId> Sessions { get; } = [SessionId.New(), SessionId.New()];

    public JobState State { get; private set; } = JobState.Draft;

    public HoldReason? Held { get; private set; }

    public string? Stuck { get; private set; }

    public JobFailure? Failure { get; private set; }

    public bool Ended { get; private set; }

    public ConnectionName? Connection { get; private set; }

    public SessionId? Session { get; private set; }

    public ResumeToken? Resume { get; private set; }

    public ModelChoice Model { get; private set; } = Given.Model;

    public IReadOnlyList<(AttemptOrigin Origin, AttemptOutcome Outcome, MessageAuthor Author, MessageAuthor Cause)> Attempts => [.. attempts.Select(attempt => (attempt.Origin, attempt.Outcome, attempt.Author, attempt.Cause))];

    private bool Started => Held != HoldReason.CouldNotStart;

    private AttemptNumber Last => new(attempts.Count);

    private bool RetriesLeft => attempts.Count - attempts.FindLastIndex(attempt => attempt.StartsRound) < attemptsPerRound;

    public static string Name(SessionId session) => session == Sessions[0] ? "first session" : "second session";

    public bool Accepts(Step step)
    {
        var copy = new JobModel(id, attemptsPerRound)
        {
            State = State, Held = Held, Stuck = Stuck, Failure = Failure, Ended = Ended, Connection = Connection, Session = Session, Resume = Resume, Model = Model, workspace = workspace,
        };
        copy.attempts.AddRange(attempts);

        return copy.Apply(step).Error is null;
    }

    public Expected Apply(Step step)
    {
        said = step.Said;

        return Answer(step);
    }

    private Expected Answer(Step step) => step.Command switch
    {
        Command.Submit => When(State == JobState.Draft, JobError.CannotSubmit, () => Move(JobState.Preparing, new JobSubmitted(id))),
        Command.Start => When(State == JobState.Preparing, JobError.CannotStart, () =>
        {
            (Connection, workspace, Session) = (Given.Connection, Given.Workspace, step.Session);
            return Move(JobState.Running, Begin(AttemptOrigin.Initial, Option<Feedback>.None, startsRound: true));
        }),
        Command.Recover => When(State == JobState.Running, JobError.CannotRecover, () =>
        {
            Join(step.Session, step.Resumed);
            Interrupt();
            return Move(JobState.Running, Begin(AttemptOrigin.Recovery, Option<Feedback>.None, startsRound: true));
        }),
        Command.Recheck => When(State == JobState.Checking, JobError.CannotRecheck, () => Move(JobState.Checking, new ChecksResumed(id, Last))),
        Command.CompleteTurn => When(State == JobState.Running, JobError.CannotCompleteTurn, () => Move(JobState.Checking, new AttemptCompleted(id, Conclude(AttemptOutcome.AwaitingCheck)))),
        Command.Pass => When(State == JobState.Checking, JobError.CannotPass, () => Move(JobState.AwaitingReview, new AttemptPassed(id, Conclude(AttemptOutcome.Passed)))),
        Command.Retry => Retry(step),
        Command.RequestHelp => When(State == JobState.Checking && !RetriesLeft, JobError.CannotRequestHelp, () => Move(JobState.NeedsHelp, new HelpRequested(id, Conclude(AttemptOutcome.Rejected)))),
        Command.Hint => When(State == JobState.NeedsHelp && Started, JobError.CannotHint, () =>
        {
            JoinWhenAsked(step);
            return Move(JobState.Running, Begin(AttemptOrigin.Hint, said, startsRound: true));
        }),
        Command.ContinueOn => ContinueOn(step),
        Command.HandOff => HandOff(step),
        Command.SendBack => SentBack(AttemptOrigin.SendBack, step),
        Command.Wake => SentBack(AttemptOrigin.Woken, step with { InSession = false }),
        Command.ContinueFromDevice => SentBack(AttemptOrigin.Device, step with { InSession = false }),
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
            (Ended, Failure) = (true, JobFailure.AgentFailed);
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
            (Held, Stuck) = (HoldReason.CouldNotStart, Problem);
            return Move(JobState.NeedsHelp, new StartFailed(id, attempts.Count == 0 ? Option<AttemptNumber>.None : Last, Problem));
        }),
        Command.RetryLaunch => When(State == JobState.NeedsHelp && !Started, JobError.CannotRetryLaunch, () =>
        {
            (Held, Stuck, Session, Resume) = (null, null, null, null);
            return Move(JobState.Preparing, new LaunchRetried(id, workspace));
        }),
        Command.Reopen => When(State == JobState.Approved, JobError.CannotReopen, () =>
        {
            Ended = false;
            JoinWhenAsked(step);
            return Move(JobState.Running, Begin(step.Revisit ? AttemptOrigin.Hint : AttemptOrigin.Reopen, said, startsRound: true));
        }),
        Command.ChooseModel => When(State is JobState.NeedsHelp or JobState.AwaitingReview, JobError.CannotChooseModel, () =>
        {
            Model = Chosen;
            return new Expected(null, new ModelChosen(id, Chosen));
        }),
        Command.JoinedBy => When(step.Cause == MessageAuthor.Person && State is JobState.Running or JobState.Checking or JobState.NeedsHelp && attempts.Count > 0 && attempts[^1].Cause != MessageAuthor.Person, JobError.CannotJoin, () =>
        {
            attempts[^1] = attempts[^1] with { Cause = MessageAuthor.Person };
            return new Expected(null, new RoundJoined(id, Last));
        }),
        Command.RecordResume => When(Session == step.Session, JobError.ForeignSession, () =>
        {
            Resume = Token;
            return new Expected(null, new ResumeRecorded(id, step.Session));
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private bool Holds(HoldReason reason) => reason switch
    {
        HoldReason.CouldNotStart => false,
        HoldReason.BudgetExceeded => State is JobState.Running or JobState.Checking or JobState.AwaitingReview,
        HoldReason.InvalidChecks => State == JobState.Checking,
        _ => State == JobState.Running,
    };

    private Expected Retry(Step step) =>
        State != JobState.Checking ? new Expected(JobError.CannotRetry, null)
        : !RetriesLeft ? new Expected(JobError.AttemptBudgetExhausted, null)
        : Move(JobState.Running, Retried(step));

    private Expected ContinueOn(Step step) =>
        Connection == step.Connection ? new Expected(JobError.SameConnection, null) : When(State == JobState.NeedsHelp && Started, JobError.CannotHint, () =>
        {
            Join(step.Session, resumed: false);
            Connection = step.Connection;
            return Move(JobState.Running, Begin(AttemptOrigin.Hint, said, startsRound: true));
        });

    private Expected HandOff(Step step)
    {
        if (Connection == step.Connection)
        {
            return new Expected(JobError.SameConnection, null);
        }

        var replacesRetry = State == JobState.Checking && RetriesLeft;

        if (!replacesRetry && !(State == JobState.NeedsHelp && Started))
        {
            return new Expected(JobError.CannotHandOff, null);
        }

        if (replacesRetry)
        {
            _ = Conclude(AttemptOutcome.Rejected);
        }

        Join(step.Session, resumed: false);
        Connection = step.Connection;

        return Move(JobState.Running, Begin(AttemptOrigin.Handoff, said, startsRound: !replacesRetry));
    }

    private Expected SentBack(AttemptOrigin origin, Step step) =>
        When(State == JobState.AwaitingReview, JobError.CannotSendBack, () =>
        {
            JoinWhenAsked(step);
            return Move(JobState.Running, Begin(origin, said, startsRound: true));
        });

    private AttemptRetried Retried(Step step)
    {
        var rejected = Conclude(AttemptOutcome.Rejected);
        JoinWhenAsked(step);

        return new AttemptRetried(id, rejected, Begin(AttemptOrigin.Retry, said, startsRound: false).Attempt, said);
    }

    private void JoinWhenAsked(Step step)
    {
        if (step.InSession)
        {
            Join(step.Session, step.Resumed);
        }
    }

    private void Join(SessionId session, bool resumed) => (Session, Resume) = (session, resumed ? Resume : null);

    private static Expected When(bool allowed, JobError rejection, Func<Expected> transition) =>
        allowed ? transition() : new Expected(rejection, null);

    private Expected Move(JobState state, IDomainEvent? happened)
    {
        State = state;

        return new Expected(null, happened);
    }

    private AttemptStarted Begin(AttemptOrigin origin, Option<Feedback> guidance, bool startsRound)
    {
        (Held, Stuck) = (null, null);
        var unsaid = origin == AttemptOrigin.Recovery ? MessageAuthor.Avala : MessageAuthor.Person;
        attempts.Add((origin, AttemptOutcome.Running, startsRound, guidance.Match(feedback => feedback.Author, () => unsaid), guidance.Match(feedback => feedback.Cause, () => unsaid)));

        return new AttemptStarted(id, Last, origin, guidance);
    }

    private AttemptNumber Conclude(AttemptOutcome outcome)
    {
        attempts[^1] = attempts[^1] with { Outcome = outcome };

        return Last;
    }

    private void Interrupt()
    {
        if (attempts is [.., (_, AttemptOutcome.Running or AttemptOutcome.AwaitingCheck, _, _, _)])
        {
            _ = Conclude(AttemptOutcome.Interrupted);
        }
    }
}
