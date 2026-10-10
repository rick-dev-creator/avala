using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.Connecting;

internal interface IForgeFile
{
    ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken);
}

internal interface IForgeTransports
{
    IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target);

    Option<ForgeError> Problem(ForgeDeclaration declaration);
}

internal sealed record ResolvedForge(ForgeName Name, IForge Forge, ForgeContext Context);

internal sealed class ForgeConnections(IForgeFile file, IEnumerable<IForge> forges, IForgeTransports transports) : IForgeCatalog
{
    public async ValueTask<TimeSpan> PollAsync(CancellationToken cancellationToken) =>
        (await file.ReadAsync(cancellationToken)).Match(settings => settings.Poll, _ => ForgeSettings.DefaultPoll);

    public async ValueTask<Result<IForge, ForgeError>> UsableAsync(ForgeName name, CancellationToken cancellationToken) =>
        (await DeclaredAsync(name, cancellationToken)).Bind(declared => Checked(declared).Map(found => found.Forge));

    public async ValueTask<Result<ResolvedForge, ForgeError>> ResolveAsync(ForgeName name, string remote, CancellationToken cancellationToken) =>
        (await DeclaredAsync(name, cancellationToken))
            .Bind(Checked)
            .Bind(found => RemoteAddresses.Parse(remote).ToResult(ForgeError.InvalidRemote).Map(address =>
            {
                var target = new ForgeTarget(found.Url, address, remote);

                return new ResolvedForge(name, found.Forge, new ForgeContext(transports.Open(found.Declaration, found.Forge, target), target));
            }));

    public async ValueTask<ForgeCatalog> CatalogAsync(CancellationToken cancellationToken)
    {
        var read = await file.ReadAsync(cancellationToken);
        var installed = forges.Select(forge => forge.Info).ToList();

        return read.Match(
            settings => new ForgeCatalog(
                installed,
                [.. settings.Forges.Select(declared => new ForgeConnectionInfo(declared.Name, declared.Forge, declared.Url, declared.Source, declared.Reference)
                {
                    Problem = Checked(declared).Match(_ => Option<ForgeError>.None, Option<ForgeError>.Some),
                })],
                Option<ForgeError>.None,
                settings.Poll),
            error => new ForgeCatalog(installed, [], error, ForgeSettings.DefaultPoll));
    }

    private async ValueTask<Result<ForgeDeclaration, ForgeError>> DeclaredAsync(ForgeName name, CancellationToken cancellationToken) =>
        (await file.ReadAsync(cancellationToken)).Bind(settings => settings.Find(name).ToResult(ForgeError.UnknownConnection));

    private Result<Usable, ForgeError> Checked(ForgeDeclaration declared)
    {
        if (forges.FirstOrDefault(forge => forge.Info.Id == declared.Forge) is not { } forge)
        {
            return ForgeError.UnknownForge;
        }

        return declared.Url.Match(Option<Uri>.Some, () => forge.Info.DefaultUrl)
            .ToResult(ForgeError.MissingUrl)
            .Bind(url => transports.Problem(declared).Match(
                problem => Result<Usable, ForgeError>.Failure(problem),
                () => new Usable(declared, forge, url)));
    }

    private sealed record Usable(ForgeDeclaration Declaration, IForge Forge, Uri Url);
}
