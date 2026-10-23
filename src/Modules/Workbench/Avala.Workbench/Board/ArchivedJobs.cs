using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;

namespace Avala.Workbench.Board;

internal sealed class ArchivedJobs(ProjectMap map, ImmutableDictionary<JobId, BoardJob> jobs, ImmutableDictionary<JobId, BoardContest> contests)
{
    public static ArchivedJobs On(JobBoard board) => new(board.Projects, board.Jobs, board.Contests);

    public bool Hides(JobId job) => Hides(job, []);

    private bool Hides(JobId job, HashSet<JobId> walked) =>
        walked.Add(job)
        && (Shelved(job) || (jobs.TryGetValue(job, out var known) && known.Summary.Parent.Match(parent => Hides(parent, walked), () => false)));

    private bool Shelved(JobId job) =>
        map.Archived.Contains(job)
        || (contests.TryGetValue(job, out var entered) && entered.Contest.Candidates.Any(candidate => map.Archived.Contains(candidate.Job)));
}
