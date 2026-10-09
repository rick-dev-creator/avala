using System.Text;
using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.BudgetFiles;

internal sealed class BudgetFileReader(IBaseFiles files) : IBudgetFiles, IRepositoryBudgets
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<BudgetFile> ReadAsync(string workingDirectory, ConnectionName connection, CancellationToken cancellationToken) =>
        For(await files.ReadAsync(workingDirectory, Breaches.BudgetFile, cancellationToken), connection);

    public async ValueTask<BudgetFile> ReadCurrentAsync(string repository, ConnectionName connection, CancellationToken cancellationToken) =>
        For(await files.ReadCurrentAsync(repository, Breaches.BudgetFile, cancellationToken), connection);

    public async ValueTask<RepositoryBudget> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var (origin, declaration) = (await files.ReadCurrentAsync(repository, Breaches.BudgetFile, cancellationToken)).Match(
            file => (Option<FileOrigin>.Some(file.Origin), file.Content.Match(Parse, Absent)),
            failure => (Option<FileOrigin>.None, Unread(failure)));

        return declaration.Match(
            found => found.Match(
                declared => new RepositoryBudget(
                    BudgetFileStatus.Applied,
                    Option<BudgetError>.None,
                    declared.Caps,
                    [.. declared.Connections.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ConnectionCaps(new ConnectionName(pair.Key), pair.Value))],
                    origin),
                () => new RepositoryBudget(BudgetFileStatus.Absent, Option<BudgetError>.None, Breaches.Unlimited, [], origin)),
            error => new RepositoryBudget(BudgetFileStatus.Rejected, error, Breaches.Unlimited, [], origin));
    }

    private static BudgetFile For(Result<BaseFile, WorkspaceFailure> read, ConnectionName connection) =>
        read.Match(
            file => new BudgetFile(file.Origin, file.Content.Match(Parse, Absent).Map(found => found.Map(declaration => declaration.For(connection)))),
            failure => new BudgetFile(Option<FileOrigin>.None, Unread(failure).Map(_ => Option<BudgetCaps>.None)));

    private static Result<Option<BudgetDeclaration>, BudgetError> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes
            ? BudgetError.TooLarge
            : BudgetFileParser.Parse(text).Map(Option<BudgetDeclaration>.Some);

    private static Result<Option<BudgetDeclaration>, BudgetError> Unread(WorkspaceFailure failure) =>
        failure == WorkspaceFailure.UnknownWorkspace ? Absent() : BudgetError.Unreadable;

    private static Result<Option<BudgetDeclaration>, BudgetError> Absent() => Option<BudgetDeclaration>.None;
}
