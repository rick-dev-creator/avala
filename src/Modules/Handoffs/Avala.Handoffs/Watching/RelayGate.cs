using Avala.Jobs.Contracts;

namespace Avala.Handoffs.Watching;

internal sealed class RelayGate(RelayLedger ledger) : ICompletionGate
{
    public const string Feedback = "This turn was wound down because the usage window is nearly spent. Go on with the task from where the work was left.";

    public int Order => 100;

    public ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(attempt.Turn.Match(turn => ledger.Unwind(turn.Session), () => false) ? GateVerdict.Relay(Feedback) : GateVerdict.Pass);
}
