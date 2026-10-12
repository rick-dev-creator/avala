using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Forges.Accounts;

internal sealed class ConnectionDesk(IForgeFileWriter file, IEnumerable<IForge> forges, IForgeCatalog catalog, IEventBus bus) : IForgeConnections
{
    public async ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(ForgeConnectionEdit connection, CancellationToken cancellationToken) =>
        await Checked(connection).Match(
            declaration => ChangeAsync(settings => With(settings, declaration), cancellationToken),
            error => Task.FromResult(Result<ForgeCatalog, ForgeError>.Failure(error)));

    public async ValueTask<Result<ForgeCatalog, ForgeError>> RemoveAsync(ForgeName name, CancellationToken cancellationToken) =>
        await ChangeAsync(
            settings => settings.Find(name).Match(
                _ => Result<ForgeSettings, ForgeError>.Success(settings with { Forges = [.. settings.Forges.Where(found => found.Name != name)] }),
                () => ForgeError.UnknownConnection),
            cancellationToken);

    private static ForgeSettings With(ForgeSettings settings, ForgeDeclaration declaration) =>
        settings with
        {
            Forges = settings.Find(declaration.Name).IsSome
                ? [.. settings.Forges.Select(found => found.Name == declaration.Name ? declaration : found)]
                : [.. settings.Forges, declaration],
        };

    private Result<ForgeDeclaration, ForgeError> Checked(ForgeConnectionEdit connection)
    {
        if (!ForgeNames.IsValid(connection.Name.Value))
        {
            return ForgeError.InvalidName;
        }

        if (forges.FirstOrDefault(forge => forge.Info.Id == connection.Forge) is not { } forge)
        {
            return ForgeError.UnknownForge;
        }

        if (connection.Url.Match(url => !url.IsAbsoluteUri || url.Scheme is not ("https" or "http"), () => false))
        {
            return ForgeError.InvalidUrl;
        }

        if (connection.Url.IsNone && forge.Info.DefaultUrl.IsNone)
        {
            return ForgeError.MissingUrl;
        }

        var reference = connection.Reference.Map(found => found.Trim()).Bind(found => found.Length > 0 ? Option<string>.Some(found) : Option<string>.None);

        return connection.Source == CredentialSource.Environment && reference.IsNone
            ? ForgeError.MissingReference
            : new ForgeDeclaration(connection.Name, connection.Forge, connection.Url, connection.Source, connection.Source == CredentialSource.None ? Option<string>.None : reference);
    }

    private async Task<Result<ForgeCatalog, ForgeError>> ChangeAsync(Func<ForgeSettings, Result<ForgeSettings, ForgeError>> change, CancellationToken cancellationToken)
    {
        if (!(await file.ChangeAsync(change, cancellationToken)).TryGetValue(out _, out var error))
        {
            return error;
        }

        var changed = await catalog.CatalogAsync(cancellationToken);
        await bus.PublishAsync(new ForgeConnectionsChanged(changed), cancellationToken);

        return changed;
    }
}
