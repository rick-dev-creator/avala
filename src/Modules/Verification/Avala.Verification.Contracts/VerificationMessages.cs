using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Verification.Contracts;

public enum VerificationOutcome
{
    Passed,
    Failed,
    NoChecksDeclared,
    InvalidDeclaration,
}

public enum CheckStatus
{
    Passed,
    Failed,
    TimedOut,
    NotFound,
    Skipped,
}

public sealed record CheckEvidence(
    string Name,
    string Command,
    CheckStatus Status,
    Option<int> ExitCode,
    TimeSpan Duration,
    string OutputTail,
    string ErrorTail);

public sealed record VerificationReport(
    JobId Job,
    int Attempt,
    VerificationOutcome Outcome,
    IReadOnlyList<CheckEvidence> Checks,
    GateVerdict Verdict,
    DateTimeOffset VerifiedAt);

public sealed record AttemptVerified(VerificationReport Report) : IIntegrationEvent;
