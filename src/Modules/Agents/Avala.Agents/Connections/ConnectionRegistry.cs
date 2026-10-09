using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Connections;

internal sealed record ResolvedConnection(ConnectionName Name, IAgentProvider Provider, ConnectionEnvironment Environment);

internal sealed class ConnectionRegistry(
    IConnectionFile file,
    IEnumerable<IAgentProvider> providers,
    IEnumerable<ICredentialSource> sources,
    IEnumerable<IConnectionDiscovery> discoveries) : IConnections
{
    private Task<IReadOnlyList<ConnectionDeclaration>>? discovering;

    public async ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        (await MergedAsync(cancellationToken)).Match(
            merged => Catalog(merged.Status, merged.Declarations),
            error => new ConnectionCatalog(ConnectionFileStatus.Rejected, error, [], Option<ConnectionName>.None));

    public async ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken)
    {
        if (connection == Option<ConnectionName>.Some(new ConnectionName(ConnectionDeclarations.Auto)))
        {
            return ConnectionError.InvalidName;
        }

        return await ChangeAsync(new DefaultChange(connection), cancellationToken);
    }

    public async ValueTask<Result<ConnectionCatalog, ConnectionError>> DeclareAsync(
        Option<ConnectionName> replacing,
        ConnectionEdit connection,
        CancellationToken cancellationToken) =>
        !ConnectionDeclaration.IsValidName(connection.Name.Value) || connection.Name.Value == ConnectionDeclarations.Auto ? ConnectionError.InvalidName
        : !providers.Any(provider => provider.Info.Id == connection.Provider) ? ConnectionError.UnknownProvider
        : connection.Credential.Match(credential => !sources.Any(source => source.Source == credential.Source), () => false) ? ConnectionError.UnknownSource
        : connection.Credential.Match(credential => string.IsNullOrWhiteSpace(credential.Reference), () => false) ? ConnectionError.MissingReference
        : await ChangeAsync(
            new DeclarationChange(
                replacing,
                connection.Name,
                connection.Provider,
                connection.Credential.Map(credential => new CredentialDeclaration(credential.Source, credential.Reference.Trim()))),
            cancellationToken);

    public async ValueTask<Result<ConnectionCatalog, ConnectionError>> RemoveAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        await ChangeAsync(new RemovalChange(connection), cancellationToken);

    private async Task<Result<ConnectionCatalog, ConnectionError>> ChangeAsync(IConnectionChange change, CancellationToken cancellationToken)
    {
        var loaded = await file.LoadAsync(cancellationToken);
        var discovered = await DiscoveredAsync(cancellationToken);
        var accepted = loaded
            .Bind(found => change.ApplyTo(found.Match(declarations => declarations, () => ConnectionDeclarations.Nothing)))
            .Bind(changed => changed.Merged(providers, discovered))
            .MapError(error => error == ConnectionError.UnknownDefault ? ConnectionError.UnknownConnection : error);

        if (!accepted.TryGetValue(out _, out var refused)
            || !(await file.ChangeAsync(change, cancellationToken)).TryGetValue(out _, out refused))
        {
            return refused;
        }

        return await CatalogAsync(cancellationToken);
    }

    public async ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken) =>
        (await ResolveAsync(connection, cancellationToken)).Map(resolved => new ConnectionInfo(resolved.Name, resolved.Provider.Info));

    public async Task<Result<ResolvedConnection, ConnectionError>> ResolveAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken)
    {
        var declared = (await MergedAsync(cancellationToken)).Bind(merged => merged.Declarations.Named(connection));

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

    private async Task<Result<MergedConnections, ConnectionError>> MergedAsync(CancellationToken cancellationToken)
    {
        var loaded = await file.LoadAsync(cancellationToken);
        var discovered = await DiscoveredAsync(cancellationToken);

        return loaded.Bind(found => found.Match(
            declarations => declarations.Merged(providers, discovered).Map(merged => new MergedConnections(ConnectionFileStatus.Applied, merged)),
            () => new MergedConnections(ConnectionFileStatus.Absent, ConnectionDeclarations.Implicit(providers, discovered))));
    }

    private async Task<IReadOnlyList<ConnectionDeclaration>> DiscoveredAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref discovering, DiscoverAsync).WaitAsync(cancellationToken);

    private async Task<IReadOnlyList<ConnectionDeclaration>> DiscoverAsync()
    {
        var found = new List<DiscoveredConnection>();

        foreach (var discovery in discoveries)
        {
            found.AddRange(await discovery.DiscoverAsync(CancellationToken.None));
        }

        return ConnectionDeclarations.Distinct(found
            .Where(Acceptable)
            .Select(ConnectionDeclaration.From));
    }

    private bool Acceptable(DiscoveredConnection discovered) =>
        ConnectionDeclaration.IsValidName(discovered.Name.Value)
        && providers.Any(provider => provider.Info.Id == discovered.Provider)
        && !string.IsNullOrWhiteSpace(discovered.Credential.Source)
        && !string.IsNullOrWhiteSpace(discovered.Credential.Reference);

    private async Task<Result<ConnectionEnvironment, ConnectionError>> CredentialAsync(
        ConnectionDeclaration declaration,
        CancellationToken cancellationToken) =>
        await declaration.Credential.Match(
            async credential => sources.FirstOrDefault(source => source.Source == credential.Source) is { } source
                ? await source.ResolveAsync(new CredentialRequest(declaration.Name, credential.Reference), cancellationToken)
                : Result<ConnectionEnvironment, ConnectionError>.Failure(ConnectionError.UnknownSource),
            () => Task.FromResult(Result<ConnectionEnvironment, ConnectionError>.Success(ConnectionEnvironment.Default)));

    private ConnectionCatalog Catalog(ConnectionFileStatus status, ConnectionDeclarations declarations) =>
        new(
            status,
            Option<ConnectionError>.None,
            [.. declarations.Connections.Select(connection => connection.Declared)],
            declarations.Default)
        {
            DefaultMode = declarations.Mode,
            Providers = [.. providers.Select(provider => provider.Info).DistinctBy(info => info.Id, StringComparer.Ordinal)],
            Sources = [.. sources.Select(source => source.Source).Distinct(StringComparer.Ordinal)],
        };

    private sealed record MergedConnections(ConnectionFileStatus Status, ConnectionDeclarations Declarations);
}
