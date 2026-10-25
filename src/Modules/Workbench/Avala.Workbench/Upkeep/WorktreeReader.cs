using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Upkeep;

internal sealed record WorktreeFolder(FolderUsage Folder, Option<BoardJob> Job);

internal sealed record UpcomingWorktree(WorktreeDue Due, Option<BoardJob> Job);

internal sealed record WorktreeState(
    IReadOnlyList<WorktreeFolder> Folders,
    Option<DiskSpace> Disk,
    WorktreePolicy Policy,
    Option<WorktreeSpace> Shortage,
    IReadOnlyList<UpcomingWorktree> Upcoming,
    WorktreeReport Report,
    DateTimeOffset Now);

internal sealed class WorktreeReader(IResources resources, IWorktreeHousekeeping housekeeping, IWorktreeHistory history, JobBoard board)
{
    public const int Days = 30;

    public async ValueTask<WorktreeState> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var sample = resources.Latest;
        var settings = await resources.SettingsAsync(cancellationToken);

        return new WorktreeState(
            sample.Match(found => found.Worktrees.Where(folder => folder.Bytes > 0).Select(folder => new WorktreeFolder(folder, folder.Job.Bind(board.Find))).ToList(), () => []),
            sample.Bind(found => found.Disk),
            settings.Worktrees,
            housekeeping.Shortage(),
            [.. housekeeping.Upcoming().Select(due => new UpcomingWorktree(due, board.Find(due.Job)))],
            await history.ReportAsync(today.AddDays(1 - Days), cancellationToken),
            now);
    }

    public ValueTask<IReadOnlyList<FreedWorktree>> FreeNowAsync(CancellationToken cancellationToken) => housekeeping.FreeNowAsync(cancellationToken);
}
