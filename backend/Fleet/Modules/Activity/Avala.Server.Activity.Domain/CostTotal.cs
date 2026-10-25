namespace Avala.Server.Activity.Domain;

internal sealed class CostTotal
{
    private CostTotal(string currency, CostBasis basis)
    {
        Currency = currency;
        Basis = basis;
    }

    public string Currency { get; }

    public CostBasis Basis { get; }

    public decimal Amount { get; private set; }

    public static void Add(ICollection<CostTotal> totals, CostAmount cost)
    {
        var total = totals.FirstOrDefault(found => found.Currency == cost.Currency && found.Basis == cost.Basis);

        if (total is null)
        {
            total = new CostTotal(cost.Currency, cost.Basis);
            totals.Add(total);
        }

        total.Amount += cost.Amount;
    }
}
