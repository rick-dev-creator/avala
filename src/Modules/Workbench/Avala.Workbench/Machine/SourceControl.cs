using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Machine;

internal sealed record SourceControlState(SourceControlAccounts Accounts, IReadOnlyList<ForgeInfo> Kinds)
{
    public static SourceControlState None { get; } = new(new SourceControlAccounts([], []), []);

    public Option<ForgeInfo> Kind(string id) => Kinds.FirstOrDefault(kind => kind.Id == id).ToOption();

    public Option<SignInTool> Tool(string kind) => Accounts.Tools.FirstOrDefault(tool => tool.Forges.Contains(kind, StringComparer.Ordinal)).ToOption();
}

internal sealed class SourceControl(ISourceControlAccounts accounts, IForgeConnections connections, IForgeCatalog catalog)
{
    public async ValueTask<SourceControlState> ReadAsync(CancellationToken cancellationToken) =>
        new(await accounts.AccountsAsync(cancellationToken), (await catalog.CatalogAsync(cancellationToken)).Forges);

    public ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(ForgeConnectionEdit connection, CancellationToken cancellationToken) =>
        connections.ConnectAsync(connection, cancellationToken);

    public ValueTask<Result<ForgeCatalog, ForgeError>> RemoveAsync(ForgeName name, CancellationToken cancellationToken) =>
        connections.RemoveAsync(name, cancellationToken);

    public ValueTask<Result<IReadOnlyList<RemoteRepository>, ForgeError>> RepositoriesAsync(ForgeName forge, string search, CancellationToken cancellationToken) =>
        accounts.RepositoriesAsync(forge, search, cancellationToken);

    public static ForgeConnectionEdit Added(AccountStatus account, Option<Uri> defaultUrl) =>
        new(account.Name, account.ForgeKind)
        {
            Url = account.Url == defaultUrl ? Option<Uri>.None : account.Url,
            Source = account.Source,
            Reference = account.Reference,
        };
}
