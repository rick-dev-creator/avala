using Avala.Fleet.Origins;
using Avala.Fleet.Recording;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Fleet.Workspace;

internal sealed class CatalogJobFacts(IJobCatalog catalog, IScratchRepositories scratch, IRepositoryOrigins origins) : IJobFacts
{
    public async ValueTask<Option<bool>> IsScratchAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => scratch.Holds(history.Summary.Repository));

    public async ValueTask<Option<JobAnnouncement>> AnnouncementAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            async history =>
            {
                var summary = history.Summary;
                var remotes = await origins.RemotesAsync(summary.Repository, cancellationToken);
                var roots = await origins.RootCommitsAsync(summary.Repository, cancellationToken);

                if (!remotes.TryGetValue(out var listed, out _) || !roots.TryGetValue(out var commits, out _))
                {
                    return Option<JobAnnouncement>.None;
                }

                return Option<JobAnnouncement>.Some(new JobAnnouncement(
                    FleetRepositoryKey.From([.. listed.Select(remote => (remote.Name, remote.Url))], commits),
                    summary.Parent.Map(parent => parent.Value),
                    summary.Model.Model,
                    summary.Model.Effort,
                    summary.Submitted));
            },
            () => Task.FromResult(Option<JobAnnouncement>.None));
}
