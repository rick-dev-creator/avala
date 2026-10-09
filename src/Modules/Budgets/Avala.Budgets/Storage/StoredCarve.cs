using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Storage;

internal sealed class StoredCarve
{
    public int Key { get; init; }

    public Guid Parent { get; init; }

    public Guid Child { get; init; }

    public string Costs { get; init; } = "[]";

    public long Tokens { get; init; }

    public double Share { get; init; }

    public long At { get; init; }

    public static StoredCarve Of(BudgetCarve carve) => new()
    {
        Parent = carve.Parent.Value,
        Child = carve.Child.Value,
        Costs = JsonSerializer.Serialize(carve.Cost.Select(cost => new StoredCost(cost.Currency, cost.Amount)).ToArray()),
        Tokens = carve.Tokens.Match(tokens => tokens, () => -1),
        Share = carve.Share,
        At = carve.At.UtcTicks,
    };

    public BudgetCarve Carve() => new(
        new JobId(Parent),
        new JobId(Child),
        [.. (JsonSerializer.Deserialize<StoredCost[]>(Costs) ?? []).Select(cost => new Cost(cost.Amount, cost.Currency))],
        Tokens < 0 ? Option<long>.None : Tokens,
        Share,
        new DateTimeOffset(At, TimeSpan.Zero));

    private sealed record StoredCost(string Currency, decimal Amount);
}
