using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Steering;

internal sealed class JobSwitching(IJobs jobs, IHandoffRequests handoffs, IConnections connections)
{
    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) => connections.CatalogAsync(cancellationToken);

    public async Task<CapabilitySet> CapabilitiesAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(found => found.Capabilities, _ => CapabilitySet.None);

    public async Task<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, ModelChoice model, CancellationToken cancellationToken) =>
        await jobs.ChooseModelAsync(job, model, cancellationToken);

    public async Task<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, ConnectionName to, CancellationToken cancellationToken) =>
        await handoffs.HandOffAsync(job, to, cancellationToken);
}
