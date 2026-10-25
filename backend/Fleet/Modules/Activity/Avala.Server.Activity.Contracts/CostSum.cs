namespace Avala.Server.Activity.Contracts;

internal sealed record CostSum(string Currency, CostBasis Basis, decimal Amount);
