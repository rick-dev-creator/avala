using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Verification.Contracts;

namespace Avala.Workbench.Reviewing;

internal enum VerdictKind
{
    Verified,
    NoChecks,
    Failed,
    InvalidDeclaration,
    Unverified,
}

internal sealed record ReviewVerdict(VerdictKind Kind, int Attempt, int Attempts);

internal interface IReviewException;

internal sealed record FailedAttempt(int Attempt, IReadOnlyList<CheckEvidence> Checks) : IReviewException;

internal sealed record PolicyDenial(PolicyDecision Decision) : IReviewException;

internal sealed record HumanDenial(HumanAnswer Answer) : IReviewException;

internal sealed record DeclinedForm(FormDecision Form) : IReviewException;

internal sealed record MadeAssumption(Assumption Assumption) : IReviewException;

internal sealed record ContinuedAfterHold(AttemptRecord Attempt) : IReviewException;

internal sealed record EditedRuleFile(string Path) : IReviewException;

internal sealed record UnreadableChanges : IReviewException;

internal static class ReviewExceptions
{
    public static ReviewVerdict VerdictOf(RunEvidence evidence) => VerdictOf(evidence.Verifications, evidence.Summary.Attempts);

    public static ReviewVerdict VerdictOf(IReadOnlyList<VerificationReport> verifications, int attempts) =>
        verifications.Count == 0
            ? new ReviewVerdict(VerdictKind.Unverified, 0, attempts)
            : Verdict(verifications[^1], attempts);

    public static IReadOnlyList<IReviewException> Of(RunEvidence evidence) =>
    [
        .. evidence.Verifications
            .Where(report => report.Outcome == VerificationOutcome.Failed)
            .Select(report => new FailedAttempt(report.Attempt, [.. report.Checks.Where(Failing)])),
        .. evidence.Denials.Select(decision => new PolicyDenial(decision)),
        .. evidence.DeniedAnswers.Select(answer => new HumanDenial(answer)),
        .. evidence.Declined.Select(form => new DeclinedForm(form)),
        .. evidence.Assumed.SelectMany(form => form.Assumptions).Select(assumption => new MadeAssumption(assumption)),
        .. evidence.Holds.Select(attempt => new ContinuedAfterHold(attempt)),
        .. evidence.RuleFiles.Select(path => new EditedRuleFile(path)),
        .. Unreadable(evidence),
    ];

    private static IReviewException[] Unreadable(RunEvidence evidence) =>
        evidence.Exceptions.Contains(ExceptionReason.ChangesUnknown) ? [new UnreadableChanges()] : [];

    private static ReviewVerdict Verdict(VerificationReport last, int attempts) =>
        new(Kind(last.Outcome), last.Attempt, Math.Max(last.Attempt, attempts));

    private static bool Failing(CheckEvidence check) => check.Status is CheckStatus.Failed or CheckStatus.TimedOut or CheckStatus.NotFound;

    private static VerdictKind Kind(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => VerdictKind.Verified,
        VerificationOutcome.NoChecksDeclared => VerdictKind.NoChecks,
        VerificationOutcome.Failed => VerdictKind.Failed,
        _ => VerdictKind.InvalidDeclaration,
    };
}
