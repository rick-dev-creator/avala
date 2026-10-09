using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Evidence;

internal sealed record JobEvidence(
    Option<AutopilotRules> Rules,
    IReadOnlyList<VerificationReport> Verifications,
    IReadOnlyList<AttemptRecord> Attempts,
    Option<IReadOnlyList<string>> ChangedFiles)
{
    public static JobEvidence None { get; } = new(Option<AutopilotRules>.None, [], [], Option<IReadOnlyList<string>>.None);

    public IReadOnlyList<PolicyDecision> Decisions { get; init; } = [];

    public IReadOnlyList<HumanAnswer> Answers { get; init; } = [];

    public IReadOnlyList<FormDecision> Forms { get; init; } = [];

    public IReadOnlyList<FileOrigin> RuleOrigins { get; init; } = [];

    public Option<ConnectionName> Connection { get; init; }

    public Option<VerificationReport> Latest => Verifications.Count == 0 ? Option<VerificationReport>.None : Verifications[^1];
}
