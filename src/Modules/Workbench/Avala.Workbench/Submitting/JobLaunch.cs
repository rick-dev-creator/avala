using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Submitting;

internal sealed class JobLaunch(IJobs jobs, IConnections connections, IConnectionPreview preview, IRepositoryPolicies policies)
{
    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        connections.CatalogAsync(cancellationToken);

    public ValueTask<Result<ConnectionPreview, JobRejection>> PreviewAsync(string repository, CancellationToken cancellationToken) =>
        preview.PreviewAsync(repository.Trim(), cancellationToken);

    public async ValueTask<Option<RepositoryPolicy>> PolicyAsync(string repository, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(repository)
            ? Option<RepositoryPolicy>.None
            : await policies.OfRepositoryAsync(repository.Trim(), cancellationToken);

    public async ValueTask<Option<OffersModels>> OfferAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(found => found.Capabilities.Get<OffersModels>(), _ => Option<OffersModels>.None);

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(
        string repository,
        string instruction,
        Option<ConnectionName> connection,
        bool supervised,
        ModelChoice model,
        CancellationToken cancellationToken) =>
        jobs.SubmitAsync(
            new JobRequest(repository.Trim(), instruction.Trim())
            {
                Connection = connection,
                Autonomy = supervised ? Autonomy.Supervised : Option<Autonomy>.None,
                Model = model,
            },
            cancellationToken);
}
