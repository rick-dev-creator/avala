namespace Avala.Jobs.Domain;

internal enum JobError
{
    EmptyInstruction,
    EmptyFeedback,
    InvalidAttemptBudget,
    CannotSubmit,
    CannotStart,
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
}
