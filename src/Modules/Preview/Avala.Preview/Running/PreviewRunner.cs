using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;

namespace Avala.Preview.Running;

internal sealed class PreviewRunner(IJobWorktrees worktrees, IProjectLaunches launches, ScriptRuns scripts, PageRuns pages)
{
    public async ValueTask<Option<Launch>> LaunchAsync(JobId job, CancellationToken cancellationToken) =>
        await (await worktrees.FindAsync(job, cancellationToken)).Match(
            worktree => launches.FindAsync(worktree, cancellationToken).AsTask(),
            () => Task.FromResult(Option<Launch>.None));

    public async ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken) =>
        await (await worktrees.FindAsync(job, cancellationToken)).Match(
            worktree => StartAsync(job, worktree, cancellationToken),
            () => Task.FromResult(Result<PreviewRun, PreviewError>.Failure(PreviewError.NoWorktree)));

    public async ValueTask<Result<PreviewRun, PreviewError>> RunSiteAsync(JobId job, string config, CancellationToken cancellationToken) =>
        await (await worktrees.FindAsync(job, cancellationToken)).Match(
            worktree => scripts.StartAsync(job, worktree, Launch.Site(config), cancellationToken).AsTask(),
            () => Task.FromResult(Result<PreviewRun, PreviewError>.Failure(PreviewError.NoWorktree)));

    public ValueTask StopAsync(Run run, CancellationToken cancellationToken) =>
        run.Tree.IsSome ? scripts.StopAsync(run, cancellationToken) : pages.StopAsync(run, cancellationToken);

    private async Task<Result<PreviewRun, PreviewError>> StartAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        await (await launches.FindAsync(worktree, cancellationToken)).Match(
            launch => launch.Kind == LaunchKind.Script
                ? scripts.StartAsync(job, worktree, launch, cancellationToken).AsTask()
                : pages.StartAsync(job, worktree, launch, cancellationToken).AsTask(),
            () => Task.FromResult(Result<PreviewRun, PreviewError>.Failure(PreviewError.NoScript)));
}
