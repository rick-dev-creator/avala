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

internal interface IGateways
{
    ValueTask<Result<int, PreviewError>> ServeFilesAsync(JobId job, string worktree, CancellationToken cancellationToken);

    ValueTask StopFilesAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<Uri, PreviewError>> ProxyAsync(PreviewTarget target, CancellationToken cancellationToken);

    ValueTask CloseAsync(JobId job, CancellationToken cancellationToken);

    IReadOnlySet<int> PortsOf(JobId job);

    ValueTask AllowNetworkAsync(JobId job, bool allowed, CancellationToken cancellationToken);
}
