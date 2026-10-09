using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Jobs;

internal sealed class Attempt(AttemptNumber number, AttemptOrigin origin, Option<Feedback> guidance, Option<SessionId> session)
{
    public AttemptNumber Number { get; } = number;

    public AttemptOrigin Origin { get; } = origin;

    public Option<Feedback> Guidance { get; } = guidance;

    public Option<SessionId> Session { get; } = session;

    public AttemptOutcome Outcome { get; private set; } = AttemptOutcome.Running;

    public bool IsUnderway => Outcome is AttemptOutcome.Running or AttemptOutcome.AwaitingCheck;

    internal void Conclude(AttemptOutcome outcome) => Outcome = outcome;
}
