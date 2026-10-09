using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Verification.Checks;

internal static class CheckOutcomes
{
    public const int TailLength = 4_000;

    private const string Cut = "[...]";

    extension(DeclaredCheck check)
    {
        public CheckEvidence Exited(int exitCode, string output, string error, TimeSpan duration) =>
            new(check.Name, check.CommandLine, exitCode == 0 ? CheckStatus.Passed : CheckStatus.Failed, exitCode, duration, Tail(output), Tail(error));

        public CheckEvidence NotFound(TimeSpan duration) => check.Without(CheckStatus.NotFound, duration);

        public CheckEvidence TimedOut(TimeSpan duration) => check.Without(CheckStatus.TimedOut, duration);

        public CheckEvidence Skipped => check.Without(CheckStatus.Skipped, TimeSpan.Zero);

        private CheckEvidence Without(CheckStatus status, TimeSpan duration) =>
            new(check.Name, check.CommandLine, status, Option<int>.None, duration, string.Empty, string.Empty);
    }

    extension(IReadOnlyList<CheckEvidence> checks)
    {
        public VerificationOutcome Outcome =>
            checks.Count == 0 ? VerificationOutcome.NoChecksDeclared
            : checks.All(check => check.Status == CheckStatus.Passed) ? VerificationOutcome.Passed
            : VerificationOutcome.Failed;

        public bool AnyFailed => checks.Any(check => check.Status != CheckStatus.Passed);
    }

    public static string Tail(string text)
    {
        var trimmed = text.TrimEnd();

        return trimmed.Length <= TailLength ? trimmed : string.Concat(Cut, trimmed.AsSpan(trimmed.Length - TailLength));
    }
}
