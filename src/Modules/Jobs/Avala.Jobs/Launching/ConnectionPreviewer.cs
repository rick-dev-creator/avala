using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Launching;

internal sealed class ConnectionPreviewer(IConnections connections, IRepositoryDefaults defaults, ConnectionChooser chooser) : IConnectionPreview
{
    public async ValueTask<Result<ConnectionPreview, JobRejection>> PreviewAsync(string repository, CancellationToken cancellationToken)
    {
        if (!(await defaults.CurrentConnectionAsync(repository, cancellationToken)).TryGetValue(out var preferred, out var rejection))
        {
            return rejection;
        }

        if (preferred.IsSome)
        {
            return new ConnectionPreview(ConnectionRoute.Repository, preferred);
        }

        var catalog = await connections.CatalogAsync(cancellationToken);

        if (catalog.Error.IsSome)
        {
            return JobRejection.UnusableConnection;
        }

        if (catalog.DefaultMode == DefaultMode.Fixed)
        {
            return new ConnectionPreview(ConnectionRoute.MachineDefault, catalog.Default);
        }

        return (await chooser.PreviewAsync(catalog, repository, cancellationToken)).Match(
            choice => new ConnectionPreview(ConnectionRoute.Capacity, choice.Connection) { Choice = choice },
            () => new ConnectionPreview(ConnectionRoute.Fallback, catalog.Default));
    }
}
