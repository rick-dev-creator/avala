using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Loops;

internal static class FailureSignatures
{
    public static FailureSignature Rejected(JobRejection rejection) => new(FailureSource.Submission, rejection.ToString());

    public static FailureSignature Of(IterationOutcome outcome, Option<HoldReason> hold, Option<VerificationReport> latest) =>
        hold.Match(
            reason => new FailureSignature(FailureSource.Hold, reason.ToString()),
            () => latest.Bind(Failing).Match(failing => failing, () => new FailureSignature(FailureSource.Job, outcome.ToString())));

    private static Option<FailureSignature> Failing(VerificationReport report) => report.Outcome switch
    {
        VerificationOutcome.Failed => report.Checks
            .FirstOrDefault(check => check.Status is not (CheckStatus.Passed or CheckStatus.Skipped))
            .ToOption()
            .Map(check => new FailureSignature(
                FailureSource.Check,
                $"{check.Name} {check.Status}{check.ExitCode.Match(code => $" exit {code}", () => string.Empty)}")),
        VerificationOutcome.InvalidDeclaration => new FailureSignature(FailureSource.Declaration, report.Outcome.ToString()),
        _ => Option<FailureSignature>.None,
    };
}
