namespace Avala.Jobs.Jobs;

internal enum JobError
{
    EmptyInstruction,
    EmptyFeedback,
    InvalidAttemptBudget,
    EmptyRepository,
    CannotSubmit,
    CannotStart,
    CannotRecover,
    CannotRecheck,
    CannotCompleteTurn,
    CannotPass,
    CannotRetry,
    AttemptBudgetExhausted,
    CannotRequestHelp,
    CannotHint,
    CannotSendBack,
    CannotApprove,
    CannotDiscard,
    CannotFail,
    CannotHold,
    ForeignSession,
    SameConnection,
    CannotHandOff,
    CannotReopen,
}
