using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Forges.Contracts;

public sealed record ForgeConnectionEdit(ForgeName Name, string Forge)
{
    public Option<Uri> Url { get; init; }

    public CredentialSource Source { get; init; }

    public Option<string> Reference { get; init; }
}

public interface IForgeConnections
{
    ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(ForgeConnectionEdit connection, CancellationToken cancellationToken);

    ValueTask<Result<ForgeCatalog, ForgeError>> RemoveAsync(ForgeName name, CancellationToken cancellationToken);
}

public sealed record ForgeConnectionsChanged(ForgeCatalog Catalog) : IIntegrationEvent;
