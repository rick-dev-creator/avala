using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Holding;

internal sealed class ControlRemotely(JobLedger ledger, IConnections connections, JobLauncher launcher)
{
    public async Task<Result<JobRemoteControlChosen, JobRejection>> ExecuteAsync(Job job, bool on, CancellationToken cancellationToken)
    {
        if (job.Parent.IsSome)
        {
            return JobRejection.NotRoot;
        }

        if (on && !await ControllableAsync(job.Connection, cancellationToken))
        {
            return JobRejection.NotRemoteControllable;
        }

        if (!job.ControlRemotely(on).IsSuccess)
        {
            return JobRejection.NotRemoteControllable;
        }

        var chosen = new JobRemoteControlChosen(job.Id, on);
        await ledger.RecordRemoteControlAsync(job, chosen, cancellationToken);

        if (on)
        {
            await launcher.ReopenForDeviceAsync(job, cancellationToken);
        }

        return chosen;
    }

    private async Task<bool> ControllableAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(found => found.Capabilities.Has<ControlsRemotely>(), _ => false);
}
