using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Verifying;

internal sealed class ChecksGate(AttemptChecks declarations, CheckRunner runner, EvidenceLedger ledger, TimeProvider clock) : ICompletionGate
{
    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        var declaration = await declarations.OfAsync(attempt, cancellationToken);
        var report = await declaration.Error.Match(
            error => Task.FromResult(Invalid(attempt, declaration.Origin, error)),
            () => declaration.Declared
                ? RunAsync(attempt, declaration, cancellationToken)
                : Task.FromResult(Report(attempt, declaration.Origin, VerificationOutcome.NoChecksDeclared, [], GateVerdict.Pass) with { Missing = true }));

        await ledger.RecordAsync(report, cancellationToken);

        return report.Verdict;
    }

    private async Task<VerificationReport> RunAsync(CompletedAttempt attempt, AttemptDeclaration declaration, CancellationToken cancellationToken)
    {
        var evidence = new List<CheckEvidence>();

        foreach (var check in declaration.Checks)
        {
            evidence.Add(evidence.AnyFailed ? check.Skipped : await runner.RunAsync(check, attempt.WorkingDirectory, cancellationToken));
        }

        return Report(attempt, declaration.Origin, evidence.Outcome, evidence, AgentFeedback.Judge(evidence)) with
        {
            Discovered = declaration.Discovered,
            DiscoveryShipped = declaration.Shipped,
        };
    }

    private VerificationReport Invalid(CompletedAttempt attempt, Option<FileOrigin> declaration, VerificationError error) =>
        Report(attempt, declaration, VerificationOutcome.InvalidDeclaration, [], GateVerdict.HoldFor(HoldReason.InvalidChecks)) with { Problem = Problem(error) };

    private static DeclarationProblem Problem(VerificationError error) => error switch
    {
        VerificationError.MissingCommand => DeclarationProblem.MissingCommand,
        VerificationError.InvalidTimeout => DeclarationProblem.InvalidTimeout,
        VerificationError.UnreadableDeclaration => DeclarationProblem.Unreadable,
        _ => DeclarationProblem.Malformed,
    };

    private VerificationReport Report(
        CompletedAttempt attempt,
        Option<FileOrigin> declaration,
        VerificationOutcome outcome,
        IReadOnlyList<CheckEvidence> checks,
        GateVerdict verdict) =>
        new(attempt.Job, attempt.Attempt, outcome, declaration, checks, AgentFeedback.Noting(verdict, declaration), clock.GetUtcNow());
}
