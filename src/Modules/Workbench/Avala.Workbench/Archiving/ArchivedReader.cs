using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Submitting;

namespace Avala.Workbench.Archiving;

internal sealed record ArchivedConversation(BoardJob Lead, string Instruction, string Project, DateTimeOffset Ended, IReadOnlyList<JobId> Jobs);

internal sealed record ArchivedShelf(string Project, IReadOnlyList<ArchivedConversation> Conversations);

internal sealed class ArchivedReader(JobBoard board, ScratchFolders scratch, TimeProvider clock)
{
    public const string Chats = "Chats";

    public DateTimeOffset Now => clock.GetUtcNow();

    public int Count => Conversations().Count;

    public bool Holds(JobId job) => board.Projects.Archived.Contains(job);

    public IReadOnlyList<ArchivedShelf> Read() =>
        [.. Conversations()
            .GroupBy(conversation => conversation.Project)
            .OrderBy(shelf => shelf.Key == Chats)
            .ThenBy(shelf => shelf.Key, StringComparer.OrdinalIgnoreCase)
            .Select(shelf => new ArchivedShelf(shelf.Key, [.. shelf.OrderByDescending(conversation => conversation.Ended)]))];

    private List<ArchivedConversation> Conversations()
    {
        var map = board.Projects;
        var jobs = board.Jobs;
        var contests = board.Contests;
        var seen = new HashSet<JobId>();
        var found = new List<ArchivedConversation>();

        foreach (var job in map.Archived.Where(jobs.ContainsKey).Select(job => jobs[job]).Where(job => job.Summary.Parent.IsNone).OrderBy(job => job.Summary.Submitted))
        {
            if (!seen.Add(job.Job))
            {
                continue;
            }

            var contest = contests.TryGetValue(job.Job, out var entered) ? entered.Contest : null;
            IReadOnlyList<JobId> shelved = contest is null ? [job.Job] : [.. contest.Candidates.Select(candidate => candidate.Job)];
            seen.UnionWith(shelved);
            found.Add(new ArchivedConversation(job, contest?.Instruction ?? job.Summary.Instruction, ProjectOf(map, job), job.Summary.Ended.Match(ended => ended, () => job.Summary.Submitted), shelved));
        }

        return found;
    }

    private string ProjectOf(ProjectMap map, BoardJob job) =>
        map.Of(job.Job, job.Summary.Repository).Match(
            project => project.Name,
            () => scratch.Holds(job.Summary.Repository) ? Chats : Path.GetFileName(Path.TrimEndingDirectorySeparator(job.Summary.Repository)));
}
