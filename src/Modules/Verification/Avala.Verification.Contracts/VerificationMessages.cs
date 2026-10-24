using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

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

public enum DeclarationProblem
{
    Malformed,
    MissingCommand,
    InvalidTimeout,
    Unreadable,
}

public enum ChecksFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public sealed record CheckDeclared(string Name, string Command, TimeSpan Timeout);

public sealed record RepositoryChecks(ChecksFileStatus File, IReadOnlyList<CheckDeclared> Checks, Option<FileOrigin> Origin);

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
    Option<FileOrigin> Declaration,
    IReadOnlyList<CheckEvidence> Checks,
    GateVerdict Verdict,
    DateTimeOffset VerifiedAt)
{
    public Option<DeclarationProblem> Problem { get; init; }

    public bool Missing { get; init; }

    public bool Discovered { get; init; }

    public bool DiscoveryShipped { get; init; }
}

public sealed record AttemptVerified(VerificationReport Report) : IIntegrationEvent;

public enum DiscoveryState
{
    Running,
    Found,
    NoneFound,
}

public sealed record ChecksDiscovery(JobId Job, string Repository, DiscoveryState State, IReadOnlyList<CheckDeclared> Checks);

public sealed record ChecksDiscoveryChanged(ChecksDiscovery Discovery) : IIntegrationEvent;

public sealed record ChecksProposal(string Content)
{
    public const string Path = "avala-checks-proposal.json";
}
