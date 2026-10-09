using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Submitting;

internal sealed class JobLaunch(IJobs jobs, IConnections connections, IConnectionPreview preview)
{
    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        connections.CatalogAsync(cancellationToken);

    public ValueTask<Result<ConnectionPreview, JobRejection>> PreviewAsync(string repository, CancellationToken cancellationToken) =>
        preview.PreviewAsync(repository.Trim(), cancellationToken);

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
