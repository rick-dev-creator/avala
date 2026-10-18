using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Board;

internal static class CompanionRelay
{
    public static ImmutableDictionary<JobId, BoardJob> Relayed(ImmutableDictionary<JobId, BoardJob> jobs, BoardJob changed) =>
        changed.Summary.Companion
            ? changed.Summary.Parent.Match(parent => Into(jobs, parent, changed), () => jobs)
            : jobs;

    public static ImmutableDictionary<JobId, BoardJob> Gathered(ImmutableDictionary<JobId, BoardJob> jobs, JobId parent) =>
        jobs.Values.Where(companion => companion.Summary.Companion && companion.Summary.Parent == parent).Aggregate(jobs, (relayed, companion) => Into(relayed, parent, companion));

    private static ImmutableDictionary<JobId, BoardJob> Into(ImmutableDictionary<JobId, BoardJob> jobs, JobId parent, BoardJob companion)
    {
        if (!jobs.TryGetValue(parent, out var known))
        {
            return jobs;
        }

        var relayed = known.Transcript.Relayed(companion.Transcript, companion.Status.IsOver);

        return ReferenceEquals(relayed, known.Transcript) ? jobs : jobs.SetItem(parent, known with { Transcript = relayed, Revision = known.Revision + 1 });
    }
}
