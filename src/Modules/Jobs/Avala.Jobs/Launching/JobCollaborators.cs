using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal static class JobCollaborators
{
    extension(IAgents agents)
    {
        public Task<Result<AgentTurn, AgentError>> TellAsync(Job job, string message, CancellationToken cancellationToken) =>
            job.Session.Match(
                session => agents.SendAsync(session, message, cancellationToken).AsTask(),
                () => Task.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.SessionClosed)));
    }

    extension(IWorkspaces workspaces)
    {
        public Task<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(Job job, CancellationToken cancellationToken) =>
            job.Workspace.Match(
                workspace => workspaces.FindAsync(workspace, cancellationToken).AsTask(),
                () => Task.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

        public Task<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(Job job, string label, CancellationToken cancellationToken) =>
            job.Workspace.Match(
                workspace => workspaces.CheckpointAsync(workspace, label, cancellationToken).AsTask(),
                () => Task.FromResult(Result<CheckpointInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));
    }
}
