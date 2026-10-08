namespace Avala.Jobs.Contracts;

public interface ICompletionGate
{
    ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken);
}

public sealed record CompletedAttempt(JobId Job, int Attempt, string WorkingDirectory, string Instruction);

public enum GateDecision
{
    Pass,
    Retry,
}

public sealed record GateVerdict(GateDecision Decision, string Feedback)
{
    public static GateVerdict Pass { get; } = new(GateDecision.Pass, string.Empty);

    public static GateVerdict Retry(string feedback) => new(GateDecision.Retry, feedback);
}
