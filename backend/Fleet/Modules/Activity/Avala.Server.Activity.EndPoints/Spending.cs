using System.Text.Json;
using Avala.Server.Activity.Contracts;

namespace Avala.Server.Activity.EndPoints;

internal sealed record TokensResponse(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite, decimal Reasoning);

internal sealed record CostResponse(string Currency, string Basis, decimal Amount);

internal static class Spending
{
    public static TokensResponse Tokens(TokenSums tokens) =>
        new(tokens.Input, tokens.Output, tokens.CacheRead, tokens.CacheWrite, tokens.Reasoning);

    public static IReadOnlyList<CostResponse> Costs(IEnumerable<CostSum> costs) =>
        [.. costs.Select(cost => new CostResponse(cost.Currency, Name(cost.Basis), cost.Amount))];

    public static string Name<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
