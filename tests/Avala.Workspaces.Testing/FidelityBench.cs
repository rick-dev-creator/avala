using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

public sealed record FidelityBench(
    WorkspaceServices Services,
    IRepositories Repositories,
    IRemoteRepositories Remotes,
    IRepositoryOrigins Origins,
    IRepositoryIndex Index,
    string Repository,
    Func<string, string, CancellationToken, Task> CommitToBase);
