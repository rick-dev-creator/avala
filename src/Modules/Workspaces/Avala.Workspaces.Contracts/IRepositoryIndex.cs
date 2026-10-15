namespace Avala.Workspaces.Contracts;

public sealed record CommitSummary(string Sha, string Subject);

public interface IRepositoryIndex
{
    ValueTask<IReadOnlyList<string>> FilesAsync(string folder, int limit, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<CommitSummary>> CommitsAsync(string folder, int count, CancellationToken cancellationToken);
}
