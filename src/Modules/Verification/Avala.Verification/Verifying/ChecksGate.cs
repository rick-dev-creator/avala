using Avala.Jobs.Contracts;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;

namespace Avala.Verification.Verifying;

internal sealed class ChecksGate(ICheckDeclarations declarations, CheckRunner runner, EvidenceLedger ledger, TimeProvider clock) : ICompletionGate
{
    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        var report = await (await declarations.ReadAsync(attempt.WorkingDirectory, cancellationToken)).Match(
            declaration => CheckDeclaration.Parse(declaration).Match(
                checks => VerifyAsync(attempt, checks, cancellationToken),
                error => Task.FromResult(Report(attempt, VerificationOutcome.InvalidDeclaration, [], AgentFeedback.Invalid(error)))),
            () => Task.FromResult(Report(attempt, VerificationOutcome.NoChecksDeclared, [], GateVerdict.Pass)));

        await ledger.RecordAsync(report, cancellationToken);

        return report.Verdict;
    }

    private async Task<VerificationReport> VerifyAsync(
        CompletedAttempt attempt,
        IReadOnlyList<DeclaredCheck> checks,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CheckEvidence>();

        foreach (var check in checks)
        {
            evidence.Add(evidence.AnyFailed ? check.Skipped : await runner.RunAsync(check, attempt.WorkingDirectory, cancellationToken));
        }

        return Report(attempt, evidence.Outcome, evidence, AgentFeedback.Judge(evidence));
    }

    private VerificationReport Report(
        CompletedAttempt attempt,
        VerificationOutcome outcome,
        IReadOnlyList<CheckEvidence> checks,
        GateVerdict verdict) =>
        new(attempt.Job, attempt.Attempt, outcome, checks, verdict, clock.GetUtcNow());
}
