using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IBaseFiles
{
    ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken);
}

public sealed record FileOrigin(string Commit, bool EditedInWorktree);

public sealed record BaseFile(string Path, FileOrigin Origin, Option<string> Content);
