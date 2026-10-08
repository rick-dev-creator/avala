namespace Avala.Jobs.Domain;

internal sealed class Attempt(AttemptNumber number, AttemptOrigin origin, Feedback? guidance)
{
    public AttemptNumber Number { get; } = number;

    public AttemptOrigin Origin { get; } = origin;

    public Feedback? Guidance { get; } = guidance;

    public AttemptOutcome Outcome { get; private set; } = AttemptOutcome.Running;

    public bool IsUnderway => Outcome is AttemptOutcome.Running or AttemptOutcome.AwaitingCheck;

    internal void Conclude(AttemptOutcome outcome) => Outcome = outcome;
}
