using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Preview.Running;

internal sealed class Previews(PreviewBook book, RunBook runs, PreviewRunner runner, IGateways gateways) : IPreviews, IHandle<JobProgressed>
{
    private static readonly JobStatus[] Ends = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    public IReadOnlyList<PreviewTarget> Of(JobId job) => book.Of(job);

    public bool IsRunning(JobId job) => runs.Of(job).IsSome;

    public async ValueTask<Option<string>> ScriptAsync(JobId job, CancellationToken cancellationToken) =>
        (await runner.LaunchAsync(job, cancellationToken)).Map(launch => launch.Name);

    public async ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken) =>
        await runs.Of(job).Match(
            running => Task.FromResult(Result<PreviewRun, PreviewError>.Success(new PreviewRun(job, running.Name))),
            () => runner.RunAsync(job, cancellationToken).AsTask());

    public async ValueTask<Result<PreviewRun, PreviewError>> RunSiteAsync(JobId job, string config, CancellationToken cancellationToken) =>
        await runs.Of(job).Match(
            running => Task.FromResult(Result<PreviewRun, PreviewError>.Success(new PreviewRun(job, running.Name))),
            () => runner.RunSiteAsync(job, config, cancellationToken).AsTask());

    public ValueTask<Result<Uri, PreviewError>> ServeFileAsync(JobId job, string path, CancellationToken cancellationToken) =>
        runner.ServeFileAsync(job, path, cancellationToken);

    public async ValueTask<Result<PreviewRun, PreviewError>> StopAsync(JobId job, CancellationToken cancellationToken) =>
        await runs.Of(job).Match<Task<Result<PreviewRun, PreviewError>>>(
            async running =>
            {
                await runner.StopAsync(running, cancellationToken);

                return new PreviewRun(job, running.Name);
            },
            () => Task.FromResult(Result<PreviewRun, PreviewError>.Failure(PreviewError.NotRunning)));

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (Ends.Contains(integrationEvent.Status))
        {
            _ = await StopAsync(integrationEvent.Job, cancellationToken);
            await gateways.CloseAsync(integrationEvent.Job, cancellationToken);
        }
    }
}
