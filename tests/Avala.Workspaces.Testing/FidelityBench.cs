using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public sealed record FidelityBench(
    WorkspaceServices Services,
    IRepositories Repositories,
    IRemoteRepositories Remotes,
    IRepositoryIndex Index,
    string Repository,
    Func<string, string, CancellationToken, Task> CommitToBase);
