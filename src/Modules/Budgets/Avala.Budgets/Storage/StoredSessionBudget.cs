using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Storage;

namespace Avala.Budgets.Storage;

internal sealed class StoredSessionBudget
{
    public int Key { get; init; }

    public Guid Session { get; init; }

    public string Connection { get; init; } = string.Empty;

    public string Budget { get; init; } = string.Empty;

    public static StoredSessionBudget Of(BudgetedSession budgeted) => new()
    {
        Session = budgeted.Budget.Session.Value,
        Connection = budgeted.Connection.Value,
        Budget = StoredJson.Write(budgeted.Budget),
    };

    public BudgetedSession Budgeted() => new(StoredJson.Read<SessionBudget>(Budget), new ConnectionName(Connection));
}
