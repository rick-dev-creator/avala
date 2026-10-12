using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;

namespace Avala.Preview.Running;

internal enum LaunchKind
{
    Script,
    Page,
}

internal sealed record Launch(string Name, LaunchKind Kind, string FileName, IReadOnlyList<string> Arguments);

internal interface IProjectLaunches
{
    ValueTask<Option<Launch>> FindAsync(string worktree, CancellationToken cancellationToken);
}

internal interface IJobWorktrees
{
    ValueTask<Option<string>> FindAsync(JobId job, CancellationToken cancellationToken);
}

internal interface IPageServers
{
    ValueTask<Result<int, PreviewError>> StartAsync(JobId job, string worktree, CancellationToken cancellationToken);

    ValueTask StopAsync(JobId job, CancellationToken cancellationToken);

    void AllowNetwork(JobId job, bool allowed);
}
