using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Caps;

internal static class Ceilings
{
    public const string MachineFile = "budgets.json";

    extension(MachineBudget machine)
    {
        public bool Ceiled => machine.SpendPerDay.Count > 0 || machine.Connections.Any(ceiling => ceiling.SpendPerDay.Count > 0);

        public Option<BudgetBreach> Declaration =>
            machine.Error.Map(error => new BudgetBreach(BudgetMeasure.Declaration, MachineFile, 0, 0, error));

        public Option<BudgetBreach> BreachBy(UsagePeriod today, ConnectionName connection) =>
            machine.Connections
                .Where(ceiling => ceiling.Connection == connection)
                .SelectMany(ceiling => Reached(
                    ceiling.SpendPerDay,
                    today.ByConnection.Where(used => used.Connection == connection).SelectMany(used => used.Usage.Costs),
                    connection))
                .Concat(Reached(machine.SpendPerDay, today.Usage.Costs, Option<ConnectionName>.None))
                .FirstOrDefault()
                .ToOption();
    }

    private static IEnumerable<BudgetBreach> Reached(IReadOnlyList<Cost> ceilings, IEnumerable<Cost> spent, Option<ConnectionName> connection) =>
        from ceiling in ceilings
        let amount = spent.Where(cost => cost.Currency == ceiling.Currency).Sum(cost => cost.Amount)
        where amount >= ceiling.Amount
        select new BudgetBreach(BudgetMeasure.DailySpend, ceiling.Currency, amount, ceiling.Amount, Option<BudgetError>.None) { Connection = connection };
}
