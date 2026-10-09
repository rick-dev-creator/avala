using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Verifying;

internal sealed class ChecksGate(IBaseFiles files, CheckRunner runner, EvidenceLedger ledger, TimeProvider clock) : ICompletionGate
{
    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        var report = await (await files.ReadAsync(attempt.WorkingDirectory, CheckDeclaration.RelativePath, cancellationToken)).Match(
            file => VerifyAsync(attempt, file, cancellationToken),
            _ => Task.FromResult(Invalid(attempt, Option<FileOrigin>.None, VerificationError.UnreadableDeclaration)));

        await ledger.RecordAsync(report, cancellationToken);

        return report.Verdict;
    }

    private Task<VerificationReport> VerifyAsync(CompletedAttempt attempt, BaseFile file, CancellationToken cancellationToken) =>
        file.Content.Match(
            declaration => CheckDeclaration.Parse(declaration).Match(
                checks => RunAsync(attempt, file.Origin, checks, cancellationToken),
                error => Task.FromResult(Invalid(attempt, file.Origin, error))),
            () => Task.FromResult(Report(attempt, file.Origin, VerificationOutcome.NoChecksDeclared, [], GateVerdict.Pass)));

    private async Task<VerificationReport> RunAsync(
        CompletedAttempt attempt,
        FileOrigin declaration,
        IReadOnlyList<DeclaredCheck> checks,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CheckEvidence>();

        foreach (var check in checks)
        {
            evidence.Add(evidence.AnyFailed ? check.Skipped : await runner.RunAsync(check, attempt.WorkingDirectory, cancellationToken));
        }

        return Report(attempt, declaration, evidence.Outcome, evidence, AgentFeedback.Judge(evidence));
    }

    private VerificationReport Invalid(CompletedAttempt attempt, Option<FileOrigin> declaration, VerificationError error) =>
        Report(attempt, declaration, VerificationOutcome.InvalidDeclaration, [], AgentFeedback.Invalid(error));

    private VerificationReport Report(
        CompletedAttempt attempt,
        Option<FileOrigin> declaration,
        VerificationOutcome outcome,
        IReadOnlyList<CheckEvidence> checks,
        GateVerdict verdict) =>
        new(attempt.Job, attempt.Attempt, outcome, declaration, checks, AgentFeedback.Noting(verdict, declaration), clock.GetUtcNow());
}
