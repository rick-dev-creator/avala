using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Enforcement;

internal interface IBudgetFiles
{
    ValueTask<BudgetFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}

internal sealed record BudgetFile(Option<FileOrigin> Origin, Result<Option<BudgetCaps>, BudgetError> Caps);
