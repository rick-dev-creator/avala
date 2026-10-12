using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Origins;
using Avala.Preview.Running;
using Avala.Sdk;

namespace Avala.Preview.Navigation;

internal sealed class PreviewDesk(IPreviews previews, PreviewBook book, IGateways gateways, PreviewFeed feed)
{
    public PreviewFeed Feed => feed;

    public Option<PreviewTarget> TargetOf(JobId job) =>
        previews.Of(job)
            .OrderBy(target => target.Source == PreviewSource.File ? 0 : target.Leased ? 1 : 2)
            .FirstOrDefault()
            .ToOption();

    public bool IsRunning(JobId job) => previews.IsRunning(job);

    public bool Serves(JobId job, Uri page) =>
        PreviewOrigins.LoopbackPort(page).IsSome && ScopeOf(job).Admits(page, Option<string>.None);

    public ValueTask<Option<string>> ScriptAsync(JobId job, CancellationToken cancellationToken) => previews.ScriptAsync(job, cancellationToken);

    public ValueTask<Result<PreviewRun, PreviewError>> RunAsync(JobId job, CancellationToken cancellationToken) => previews.RunAsync(job, cancellationToken);

    public ValueTask<Result<PreviewRun, PreviewError>> StopAsync(JobId job, CancellationToken cancellationToken) => previews.StopAsync(job, cancellationToken);

    public ValueTask AllowNetworkAsync(JobId job, bool allowed, CancellationToken cancellationToken) =>
        gateways.AllowNetworkAsync(job, allowed, cancellationToken);

    public ValueTask<Result<Uri, PreviewError>> GatewayAsync(PreviewTarget target, CancellationToken cancellationToken) =>
        gateways.ProxyAsync(target, cancellationToken);

    public SurfaceRules Rules(JobId job, Func<bool> network) =>
        new(
            address => ScopeOf(job).Admits(address, RealFiles.Of(address)),
            address => ScopeOf(job).AdmitsResource(address, network(), RealFiles.Of(address)));

    private PreviewScope ScopeOf(JobId job)
    {
        var scope = book.ScopeOf(job);

        return scope with { Listening = scope.Listening.Union(gateways.PortsOf(job)) };
    }
}
