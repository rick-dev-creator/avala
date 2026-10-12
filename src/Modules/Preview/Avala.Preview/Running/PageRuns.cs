using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Preview.Running;

internal sealed class PageRuns(IPageServers pages, PreviewBook book, RunBook runs, IEventBus bus)
{
    public async ValueTask<Result<PreviewRun, PreviewError>> StartAsync(JobId job, string worktree, Launch launch, CancellationToken cancellationToken)
    {
        if (!(await pages.StartAsync(job, worktree, cancellationToken)).TryGetValue(out var port, out var error))
        {
            return error;
        }

        var page = new PreviewTarget(job, Address(port, launch.Name), PreviewSource.File) { Leased = true };
        book.Serve(page);
        await runs.StartedAsync(new Run(job, launch.Name, Option<ProcessTreeId>.None), cancellationToken);
        await bus.PublishAsync(new PreviewAvailable(page), cancellationToken);

        return new PreviewRun(job, launch.Name);
    }

    public async ValueTask StopAsync(Run run, CancellationToken cancellationToken)
    {
        await pages.StopAsync(run.Job, cancellationToken);
        _ = await runs.EndedAsync(run.Job, current => current == run, cancellationToken);
        await book.Unserve(run.Job).Match(
            page => bus.PublishAsync(new PreviewGone(page), cancellationToken).AsTask(),
            () => Task.CompletedTask);
    }

    private static Uri Address(int port, string page) =>
        new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/{string.Join('/', page.Split('/').Select(Uri.EscapeDataString))}"));
}
