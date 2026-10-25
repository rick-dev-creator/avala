using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IRepositoryOrigins
{
    ValueTask<Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>> RemotesAsync(string repository, CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> RootCommitsAsync(string repository, CancellationToken cancellationToken);
}

public sealed record OriginRemote(string Name, string Url);
