using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class JobLauncher(JobLedger ledger, IWorkspaces workspaces, IAgents agents)
{
    public async Task LaunchAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Preparing)
        {
            return;
        }

        if (!(await workspaces.PrepareAsync(new WorkspaceRequest(job.Repository.Value), cancellationToken)).TryGetValue(out var workspace, out _))
        {
            await FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken);
            return;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path), cancellationToken)).TryGetValue(out var session, out _))
        {
            await FailAsync(job, FailureReason.AgentUnavailable, cancellationToken);
            return;
        }

        if (job.Start(workspace.Id, session).IsSuccess)
        {
            await BeginAsync(job, session, cancellationToken);
        }
    }

    public async Task RelaunchAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State is not (JobState.Running or JobState.Checking))
        {
            return;
        }

        if (!(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            await FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken);
            return;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path), cancellationToken)).TryGetValue(out var session, out _))
        {
            await FailAsync(job, FailureReason.AgentUnavailable, cancellationToken);
            return;
        }

        if (job.Recover(session).IsSuccess)
        {
            await BeginAsync(job, session, cancellationToken);
        }
    }

    private async Task BeginAsync(Job job, SessionId session, CancellationToken cancellationToken)
    {
        await ledger.RecordSessionAsync(job, session, cancellationToken);

        if ((await agents.TellAsync(job, job.Instruction.Text, cancellationToken)).IsFailure)
        {
            await FailAsync(job, FailureReason.AgentUnavailable, cancellationToken);
        }
    }

    private async Task FailAsync(Job job, FailureReason reason, CancellationToken cancellationToken)
    {
        if (job.Fail(reason).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
        }
    }
}
