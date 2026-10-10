using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Launching;

internal interface IRepositoryDefaults
{
    ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken);

    ValueTask<Result<Option<ConnectionName>, JobRejection>> CurrentConnectionAsync(string repository, CancellationToken cancellationToken);

    ValueTask<Result<ModelChoice, JobRejection>> ModelAsync(string worktree, CancellationToken cancellationToken);

    ValueTask<ModelChoice> CurrentModelAsync(string repository, CancellationToken cancellationToken);

    ValueTask<Result<Option<string>, JobRejection>> ApprovalAsync(string worktree, CancellationToken cancellationToken);
}
