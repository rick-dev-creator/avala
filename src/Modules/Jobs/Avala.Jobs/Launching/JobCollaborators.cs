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
                session => job.State == JobState.Running
                    ? agents.SayOrJoinAsync(session, new UserTurn(message) { Attachments = job.Attempts.Count == 0 ? [] : job.Attempts[^1].Attachments }, cancellationToken)
                    : Task.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.Unsupported)),
                () => Task.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.SessionClosed)));

        private async Task<Result<AgentTurn, AgentError>> SayOrJoinAsync(SessionId session, UserTurn turn, CancellationToken cancellationToken)
        {
            var said = await agents.SayAsync(session, turn, cancellationToken);

            return !said.TryGetValue(out _, out var error) && error == AgentError.TurnInProgress
                ? await agents.SayAsync(session, turn with { MidTurn = true }, cancellationToken)
                : said;
        }
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
