namespace Avala.Server.Activity.EndPoints.Usage;

internal sealed record ListUsageResponse(
    string From,
    string To,
    string Zone,
    string Step,
    CostKindResponse? Reference,
    UsageTotalsResponse Totals,
    IReadOnlyList<UsagePeriodResponse> Series,
    IReadOnlyList<UsageRepositoryResponse> Repositories,
    IReadOnlyList<UsageInstanceResponse> Instances,
    IReadOnlyList<UsageModelResponse> Models);

internal sealed record CostKindResponse(string Currency, string Basis);

internal sealed record UsageTotalsResponse(TokensResponse Tokens, IReadOnlyList<CostResponse> Costs, int Jobs, int Instances);

internal sealed record UsagePeriodResponse(string Start, TokensResponse Tokens, IReadOnlyList<CostResponse> Costs);

internal sealed record UsageRepositoryResponse(string Key, string Kind, TokensResponse Tokens, IReadOnlyList<CostResponse> Costs, int Jobs);

internal sealed record UsageInstanceResponse(string Instance, string Name, TokensResponse Tokens, IReadOnlyList<CostResponse> Costs, int Jobs);

internal sealed record UsageModelResponse(string Model, TokensResponse Tokens, IReadOnlyList<CostResponse> Costs);
