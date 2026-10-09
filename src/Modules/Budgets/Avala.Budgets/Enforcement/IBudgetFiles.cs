using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Enforcement;

internal interface IBudgetFiles
{
    ValueTask<BudgetFile> ReadAsync(string workingDirectory, ConnectionName connection, CancellationToken cancellationToken);

    ValueTask<BudgetFile> ReadCurrentAsync(string repository, ConnectionName connection, CancellationToken cancellationToken);
}

internal sealed record BudgetFile(Option<FileOrigin> Origin, Result<Option<BudgetCaps>, BudgetError> Caps);
