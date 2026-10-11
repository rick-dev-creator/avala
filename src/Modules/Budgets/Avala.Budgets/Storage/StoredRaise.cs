using Avala.Budgets.Contracts;
using Avala.Storage;

namespace Avala.Budgets.Storage;

internal sealed class StoredRaise
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public string Raise { get; init; } = string.Empty;

    public static StoredRaise Of(BudgetRaise raise) => new() { Job = raise.Job.Value, Raise = StoredJson.Write(raise) };

    public BudgetRaise Read() => StoredJson.Read<BudgetRaise>(Raise);
}
