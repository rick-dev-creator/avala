using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class JobLauncher(JobLedger ledger, IWorkspaces workspaces, IAgents agents, IRepositoryDefaults defaults)
{
    public const string RestartNote = "The harness restarted while you were working on this job. Continue where you left off.";

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

        if (!(await ConnectionOfAsync(job, workspace, cancellationToken)).TryGetValue(out var connection, out _))
        {
            await FailAsync(job, FailureReason.ConnectionUnavailable, cancellationToken);
            return;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path) { Connection = connection }, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            await FailAsync(job, Failure(error), cancellationToken);
            return;
        }

        if (job.Start(workspace.Id, opened.Session, opened.Connection).IsSuccess)
        {
            await BeginAsync(job, opened.Session, job.Instruction.Text, cancellationToken);
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

        if (!(await OpenAsync(job, workspace, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            await FailAsync(job, Failure(error), cancellationToken);
            return;
        }

        if (job.Recover(opened.Session, opened.Resumed).IsSuccess)
        {
            await BeginAsync(job, opened.Session, opened.Resumed ? RestartNote : job.Instruction.Text, cancellationToken);
        }
    }

    public async Task<Result<JobContinuation, JobRejection>> ContinueAsync(Job job, Feedback guidance, CancellationToken cancellationToken)
    {
        if (job.State != JobState.NeedsHelp)
        {
            return JobRejection.NotHeld;
        }

        var live = job.Session.Bind(session => agents.IsOpen(session) ? Option<SessionId>.Some(session) : Option<SessionId>.None);

        return await live.Match(
            session => ContinueInAsync(job, session, guidance, cancellationToken),
            () => ContinueInNewSessionAsync(job, guidance, cancellationToken));
    }

    private async Task<Result<JobContinuation, JobRejection>> ContinueInAsync(
        Job job,
        SessionId session,
        Feedback guidance,
        CancellationToken cancellationToken)
    {
        _ = job.Hint(guidance);
        await ledger.RecordAsync(job, cancellationToken);
        await TellAsync(job, guidance.Text, cancellationToken);

        return new JobContinuation(job.Id, session, ContinuedIn.SameSession);
    }

    private async Task<Result<JobContinuation, JobRejection>> ContinueInNewSessionAsync(
        Job job,
        Feedback guidance,
        CancellationToken cancellationToken)
    {
        if (!(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        if (!(await OpenAsync(job, workspace, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            return error switch
            {
                AgentError.UnknownConnection => JobRejection.UnknownConnection,
                AgentError.UnusableConnection => JobRejection.UnusableConnection,
                _ => JobRejection.AgentUnavailable,
            };
        }

        _ = job.Hint(guidance, opened.Session, opened.Resumed);
        await BeginAsync(
            job,
            opened.Session,
            opened.Resumed ? guidance.Text : $"{job.Instruction.Text}\n\n{guidance.Text}",
            cancellationToken);

        return new JobContinuation(job.Id, opened.Session, opened.Resumed ? ContinuedIn.ResumedConversation : ContinuedIn.NewConversation);
    }

    private async Task<Result<OpenedSession, AgentError>> OpenAsync(Job job, WorkspaceInfo workspace, CancellationToken cancellationToken) =>
        await agents.OpenAsync(new AgentRequest(workspace.Path) { Resume = job.Resume, Connection = job.Connection }, cancellationToken);

    private async Task<Result<Option<ConnectionName>, JobRejection>> ConnectionOfAsync(
        Job job,
        WorkspaceInfo workspace,
        CancellationToken cancellationToken) =>
        job.Connection.IsSome ? job.Connection : await defaults.ConnectionAsync(workspace.Path, cancellationToken);

    private static FailureReason Failure(AgentError error) =>
        error is AgentError.UnknownConnection or AgentError.UnusableConnection ? FailureReason.ConnectionUnavailable : FailureReason.AgentUnavailable;

    private async Task BeginAsync(Job job, SessionId session, string message, CancellationToken cancellationToken)
    {
        await ledger.RecordSessionAsync(job, session, cancellationToken);
        await TellAsync(job, message, cancellationToken);
    }

    private async Task TellAsync(Job job, string message, CancellationToken cancellationToken)
    {
        if ((await agents.TellAsync(job, message, cancellationToken)).IsFailure)
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
