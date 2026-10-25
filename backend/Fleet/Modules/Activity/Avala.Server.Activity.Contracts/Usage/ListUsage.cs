using Ardalis.Result;
using Mediator;

namespace Avala.Server.Activity.Contracts.Usage;

internal sealed record ListUsage(string? From, string? To, string? Zone, string? Step, string? Repository) : IQuery<Result<UsageReport>>;

internal sealed record UsageReport(
    DateOnly From,
    DateOnly To,
    string Zone,
    string Step,
    CostKind? Reference,
    UsageTotals Totals,
    IReadOnlyList<UsagePeriodSum> Series,
    IReadOnlyList<UsageShare> Repositories,
    IReadOnlyList<UsageShare> Instances,
    IReadOnlyList<UsageShare> Models);

internal sealed record CostKind(string Currency, CostBasis Basis);

internal sealed record UsageTotals(TokenSums Tokens, IReadOnlyList<CostSum> Costs, int Jobs, int Instances);

internal sealed record UsagePeriodSum(DateOnly Start, TokenSums Tokens, IReadOnlyList<CostSum> Costs);

internal enum UsageShareKind
{
    Repository,
    Unknown,
    OutsideAnyJob,
    Instance,
    Model,
}

internal sealed record UsageShare(UsageShareKind Kind, string Key, string Name, TokenSums Tokens, IReadOnlyList<CostSum> Costs, int Jobs);
