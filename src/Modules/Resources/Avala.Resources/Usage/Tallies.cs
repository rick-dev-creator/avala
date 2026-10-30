using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Usage;

internal static class Tallies
{
    public static ResourceUsage Nothing { get; } = new(0, 0, TimeSpan.Zero, 0, [], 0);

    public static ResourceUsage Tally(IEnumerable<TreeUsage> trees, long diskBytes)
    {
        var counted = trees.ToList();
        var processes = counted.SelectMany(tree => tree.Processes).ToList();

        return new ResourceUsage(
            processes.Count,
            processes.Sum(process => process.MemoryBytes),
            processes.Aggregate(TimeSpan.Zero, (total, process) => total + process.CpuTime),
            counted.Sum(tree => tree.CpuLoad),
            [.. processes.SelectMany(process => process.Ports).Distinct().Order()],
            diskBytes);
    }

    public static double Load(IReadOnlyDictionary<int, TimeSpan> before, IReadOnlyList<ProcessUsage> now, TimeSpan elapsed) =>
        elapsed <= TimeSpan.Zero
            ? 0
            : now.Where(process => before.ContainsKey(process.Id))
                .Sum(process => Math.Max(0, (process.CpuTime - before[process.Id]).TotalSeconds)) / elapsed.TotalSeconds;

    public static Option<TimeSpan> DeletedAfter(this WorktreePolicy policy, JobStatus status) => status switch
    {
        JobStatus.Discarded => policy.DeleteDiscarded,
        JobStatus.Failed => policy.DeleteFailed,
        _ => Option<TimeSpan>.None,
    };

    public static WorktreeMoment EndedAs(this JobStatus status, bool delivered, bool awaitsMerge) => status switch
    {
        JobStatus.Discarded => WorktreeMoment.Discarded,
        JobStatus.Failed => WorktreeMoment.Failed,
        _ when delivered => WorktreeMoment.Delivered,
        _ when awaitsMerge => WorktreeMoment.PullRequest,
        _ => WorktreeMoment.Undelivered,
    };

    public static bool Ended(this JobStatus status) => status is JobStatus.Discarded or JobStatus.Failed or JobStatus.Approved;
}
