using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Origins;
using Avala.Preview.Running;
using Avala.Preview.Surfaces;
using Avala.Sdk;

namespace Avala.Preview.Navigation;

internal sealed class PreviewDesk(IPreviews previews, PreviewBook book, IPageServers pages, PreviewFeed feed)
{
    public PreviewFeed Feed => feed;

    public Option<PreviewTarget> TargetOf(JobId job) =>
        previews.Of(job)
            .OrderBy(target => target.Source == PreviewSource.File ? 0 : target.Leased ? 1 : 2)
            .FirstOrDefault()
            .ToOption();

    public bool IsRunning(JobId job) => previews.IsRunning(job);

    public ValueTask<Option<string>> ScriptAsync(JobId job, CancellationToken cancellationToken) => previews.ScriptAsync(job, cancellationToken);

    public ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken) => previews.RunAsync(job, cancellationToken);

    public ValueTask<Result<PreviewRun, PreviewError>> StopAsync(JobId job, CancellationToken cancellationToken) => previews.StopAsync(job, cancellationToken);

    public void AllowNetwork(JobId job, bool allowed) => pages.AllowNetwork(job, allowed);

    public SurfaceRules Rules(JobId job, Func<bool> network) =>
        new(
            address => book.ScopeOf(job).Admits(address, RealFiles.Of(address)),
            address => book.ScopeOf(job).AdmitsResource(address, network(), RealFiles.Of(address)));
}
