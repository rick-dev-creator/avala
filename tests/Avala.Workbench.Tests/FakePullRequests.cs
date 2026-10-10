using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakePullRequests : IPullRequests
{
    public Option<PullRequestOffer> Offer { get; set; }

    public Dictionary<JobId, PullRequestWatchState> Watches { get; } = [];

    public Dictionary<JobId, ForgeError> Refusals { get; } = [];

    public List<JobId> Refreshed { get; } = [];

    public ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Offer);

    public Option<PullRequestWatchState> Of(JobId job) => Watches.TryGetValue(job, out var state) ? state : Option<PullRequestWatchState>.None;

    public IReadOnlyList<WakeUpRecord> WakeUpsOf(JobId job) => [];

    public Option<ForgeError> RefusalOf(JobId job) => Refusals.TryGetValue(job, out var error) ? error : Option<ForgeError>.None;

    public ValueTask<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken)
    {
        Refreshed.Add(job);

        return ValueTask.FromResult(Of(job).ToResult(ForgeError.NotFound));
    }
}

internal sealed class FakeForgeCatalog : IForgeCatalog
{
    public ForgeCatalog Catalog { get; set; } = new([], [], Option<ForgeError>.None, TimeSpan.FromSeconds(60));

    public ValueTask<ForgeCatalog> CatalogAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Catalog);
}
