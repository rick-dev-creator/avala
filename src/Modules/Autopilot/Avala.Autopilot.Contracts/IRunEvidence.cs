using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Contracts;

public interface IRunEvidence
{
    ValueTask<Option<RunEvidence>> OfJobAsync(JobId job, CancellationToken cancellationToken);
}

public sealed record RunEvidence(JobId Job, EvidenceSummary Summary, IReadOnlyList<ExceptionReason> Exceptions)
{
    public IReadOnlyList<VerificationReport> Verifications { get; init; } = [];

    public IReadOnlyList<PolicyDecision> Denials { get; init; } = [];

    public IReadOnlyList<HumanAnswer> DeniedAnswers { get; init; } = [];

    public IReadOnlyList<FormDecision> Declined { get; init; } = [];

    public IReadOnlyList<FormDecision> Assumed { get; init; } = [];

    public IReadOnlyList<string> RuleFiles { get; init; } = [];

    public IReadOnlyList<AttemptRecord> Holds { get; init; } = [];

    public int AllowedByRules { get; init; }
}
