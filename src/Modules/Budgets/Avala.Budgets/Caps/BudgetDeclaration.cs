using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Contracts;

namespace Avala.Budgets.Caps;

internal sealed record BudgetDeclaration(BudgetCaps Caps)
{
    public IReadOnlyDictionary<string, BudgetCaps> Connections { get; init; } = ImmutableDictionary<string, BudgetCaps>.Empty;

    public BudgetCaps For(ConnectionName connection) =>
        connection.Value is { } name && Connections.TryGetValue(name, out var caps) ? caps : Caps;
}
