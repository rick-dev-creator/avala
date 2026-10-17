using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IWorkingCommits
{
    ValueTask<Result<WorkingCommit, WorkspaceFailure>> CommitAsync(string repository, string path, string message, CancellationToken cancellationToken);
}

public sealed record WorkingCommit(string Commit, string Branch);
