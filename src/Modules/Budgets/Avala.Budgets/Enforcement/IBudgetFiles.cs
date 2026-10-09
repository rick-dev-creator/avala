using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal interface IBudgetFiles
{
    ValueTask<Result<Option<BudgetCaps>, BudgetError>> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}
