using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Submitting;

internal sealed class JobLaunch(IJobs jobs, IConnections connections)
{
    public async ValueTask<IReadOnlyList<ConnectionName>> ConnectionsAsync(CancellationToken cancellationToken) =>
        [.. (await connections.CatalogAsync(cancellationToken)).Connections.Select(connection => connection.Name)];

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(
        string repository,
        string instruction,
        Option<ConnectionName> connection,
        bool supervised,
        CancellationToken cancellationToken) =>
        jobs.SubmitAsync(
            new JobRequest(repository.Trim(), instruction.Trim())
            {
                Connection = connection,
                Autonomy = supervised ? Autonomy.Supervised : Option<Autonomy>.None,
            },
            cancellationToken);
}
