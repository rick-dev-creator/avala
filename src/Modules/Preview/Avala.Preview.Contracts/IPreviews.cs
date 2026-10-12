using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Preview.Contracts;

public interface IPreviews
{
    IReadOnlyList<PreviewTarget> Of(JobId job);

    bool IsRunning(JobId job);

    ValueTask<Option<string>> ScriptAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<PreviewRun, PreviewError>> StopAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<PreviewTarget, PreviewError>> ServeAsync(JobId job, string page, CancellationToken cancellationToken);
}
