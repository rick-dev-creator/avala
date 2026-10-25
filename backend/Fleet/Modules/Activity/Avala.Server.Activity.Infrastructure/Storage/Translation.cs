using Avala.Server.Activity.Domain;
using Contract = Avala.Server.Activity.Contracts;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal static class Translation
{
    public static JobStatus ToDomain(this Contract.JobStatus status) => (JobStatus)(int)status;

    public static HoldReason ToDomain(this Contract.HoldReason reason) => (HoldReason)(int)reason;

    public static VerificationOutcome ToDomain(this Contract.VerificationOutcome outcome) => (VerificationOutcome)(int)outcome;

    public static CostBasis ToDomain(this Contract.CostBasis basis) => (CostBasis)(int)basis;

    public static Contract.JobStatus ToContract(this JobStatus status) => (Contract.JobStatus)(int)status;

    public static Contract.HoldReason ToContract(this HoldReason reason) => (Contract.HoldReason)(int)reason;

    public static Contract.VerificationOutcome ToContract(this VerificationOutcome outcome) => (Contract.VerificationOutcome)(int)outcome;

    public static Contract.CostBasis ToContract(this CostBasis basis) => (Contract.CostBasis)(int)basis;

    public static Contract.TokenSums ToContract(this TokenTotals tokens) =>
        new(tokens.Input, tokens.Output, tokens.CacheRead, tokens.CacheWrite, tokens.Reasoning);

    public static Contract.RepositoryJobs.RepositoryJob ToContract(this FleetJob job, string instanceName) =>
        new(
            job.InstanceId,
            instanceName,
            job.JobId,
            job.Status?.ToContract(),
            job.VisibleHold?.ToContract(),
            job.Attempts,
            job.LastOutcome?.ToContract(),
            job.Tokens.ToContract(),
            job.Costs.ToContract(),
            job.FirstActivityAt,
            job.LastActivityAt);

    public static IReadOnlyList<Contract.CostSum> ToContract(this IEnumerable<CostTotal> costs) =>
        [.. costs.OrderBy(cost => cost.Currency, StringComparer.Ordinal).ThenBy(cost => cost.Basis).Select(cost => new Contract.CostSum(cost.Currency, cost.Basis.ToContract(), cost.Amount))];
}
