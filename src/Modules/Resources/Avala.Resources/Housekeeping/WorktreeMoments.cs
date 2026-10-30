using Avala.Forges.Contracts;
using Avala.Projects.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Reaping;
using Avala.Sdk.Events;

namespace Avala.Resources.Housekeeping;

internal sealed class WorktreeMoments(WorktreeHousekeeper housekeeper, OrphanReaper reaper) : IHandle<ConversationsArchived>, IHandle<PullRequestWatchChanged>
{
    public async ValueTask HandleAsync(ConversationsArchived integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in integrationEvent.Jobs)
        {
            await housekeeper.MarkAsync(job, WorktreeMoment.Archived, reaper, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(PullRequestWatchChanged integrationEvent, CancellationToken cancellationToken) =>
        await integrationEvent.State.Ended.Match(
            end => end == WatchEnd.Merged ? housekeeper.MarkAsync(integrationEvent.State.Job, WorktreeMoment.Delivered, reaper, cancellationToken) : Task.CompletedTask,
            () => housekeeper.MarkAsync(integrationEvent.State.Job, WorktreeMoment.PullRequest, reaper, cancellationToken));
}
