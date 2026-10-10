using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Contracts;

public interface ICompletionGate
{
    ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken);
}

public sealed record CompletedAttempt(JobId Job, int Attempt, string WorkingDirectory, string Instruction)
{
    public Option<FinishedTurn> Turn { get; init; }
}

public sealed record FinishedTurn(SessionId Session, TurnId Turn);

public enum GateDecision
{
    Pass,
    Retry,
    Hold,
}

public sealed record GateVerdict(GateDecision Decision, string Feedback)
{
    public static GateVerdict Pass { get; } = new(GateDecision.Pass, string.Empty);

    public Option<HoldReason> Hold { get; private init; }

    public static GateVerdict Retry(string feedback) => new(GateDecision.Retry, feedback);

    public static GateVerdict HoldFor(HoldReason reason) => new(GateDecision.Hold, string.Empty) { Hold = reason };
}
