using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Connections;

internal sealed record ResolvedConnection(ConnectionName Name, IAgentProvider Provider, ConnectionEnvironment Environment);

internal sealed class ConnectionRegistry(
    IConnectionFile file,
    IEnumerable<IAgentProvider> providers,
    IEnumerable<ICredentialSource> sources) : IConnections
{
    public async ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        (await file.LoadAsync(cancellationToken)).Match(
            declared => declared.Match(
                declarations => Catalog(ConnectionFileStatus.Applied, declarations),
                () => Catalog(ConnectionFileStatus.Absent, ConnectionDeclarations.Implicit(providers))),
            error => new ConnectionCatalog(ConnectionFileStatus.Rejected, error, [], Option<ConnectionName>.None));

    public async ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken) =>
        (await ResolveAsync(connection, cancellationToken)).Map(resolved => new ConnectionInfo(resolved.Name, resolved.Provider.Info));

    public async Task<Result<ResolvedConnection, ConnectionError>> ResolveAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken)
    {
        var declared = (await file.LoadAsync(cancellationToken))
            .Map(found => found.Match(declarations => declarations, () => ConnectionDeclarations.Implicit(providers)))
            .Bind(declarations => declarations.Named(connection));

        if (!declared.TryGetValue(out var declaration, out var error))
        {
            return error;
        }

        if (providers.FirstOrDefault(provider => provider.Info.Id == declaration.Provider) is not { } registered)
        {
            return ConnectionError.UnknownProvider;
        }

        return (await CredentialAsync(declaration, cancellationToken)).Map(environment =>
            new ResolvedConnection(declaration.Name, registered, environment with { Settings = declaration.Settings }));
    }

    private async Task<Result<ConnectionEnvironment, ConnectionError>> CredentialAsync(
        ConnectionDeclaration declaration,
        CancellationToken cancellationToken) =>
        await declaration.Credential.Match(
            async credential => sources.FirstOrDefault(source => source.Source == credential.Source) is { } source
                ? await source.ResolveAsync(new CredentialRequest(declaration.Name, credential.Reference), cancellationToken)
                : Result<ConnectionEnvironment, ConnectionError>.Failure(ConnectionError.UnknownSource),
            () => Task.FromResult(Result<ConnectionEnvironment, ConnectionError>.Success(ConnectionEnvironment.Default)));

    private static ConnectionCatalog Catalog(ConnectionFileStatus status, ConnectionDeclarations declarations) =>
        new(
            status,
            Option<ConnectionError>.None,
            [.. declarations.Connections.Select(connection => connection.Declared)],
            declarations.Connections.Count == 0 ? Option<ConnectionName>.None : declarations.Default);
}
