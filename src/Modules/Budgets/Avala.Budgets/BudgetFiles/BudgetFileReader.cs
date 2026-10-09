using System.Text;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.BudgetFiles;

internal sealed class BudgetFileReader(IBaseFiles files) : IBudgetFiles
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<BudgetFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
        (await files.ReadAsync(workingDirectory, Breaches.BudgetFile, cancellationToken)).Match(
            file => new BudgetFile(file.Origin, file.Content.Match(Parse, Absent)),
            failure => new BudgetFile(Option<FileOrigin>.None, failure == WorkspaceFailure.UnknownWorkspace ? Absent() : BudgetError.Unreadable));

    private static Result<Option<BudgetCaps>, BudgetError> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes
            ? BudgetError.TooLarge
            : BudgetFileParser.Parse(text).Map(Option<BudgetCaps>.Some);

    private static Result<Option<BudgetCaps>, BudgetError> Absent() => Option<BudgetCaps>.None;
}
