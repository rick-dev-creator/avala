using System.Collections.Immutable;
using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Navigation;
using Avala.Preview.Origins;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Preview.Detection;

internal static class Sightings
{
    private static readonly StringComparer Folders = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static ImmutableDictionary<JobId, Sighting> Of(
        ResourceSample sample,
        IReadOnlyList<PortLease> leases,
        Func<ProcessTreeId, Option<JobId>> owner,
        IReadOnlySet<JobId> ended) =>
        sample.Trees
            .SelectMany(tree => tree.Job.Match(Option<JobId>.Some, () => owner(tree.Tree)).Match<(TreeUsage Tree, JobId Job)[]>(job => [(tree, job)], () => []))
            .Where(attributed => !ended.Contains(attributed.Job))
            .GroupBy(attributed => attributed.Job)
            .ToImmutableDictionary(group => group.Key, group => Sight(group.Key, [.. group.Select(attributed => attributed.Tree)], leases));

    public static Uri Address(int port) => new(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/"));

    private static Sighting Sight(JobId job, IReadOnlyList<TreeUsage> trees, IReadOnlyList<PortLease> leases)
    {
        var worktree = trees[0].Home;
        var lease = leases.FirstOrDefault(leased => Folders.Equals(leased.Worktree, worktree)).ToOption().Map(leased => new PortBlock(leased.First, leased.Last));
        var listening = trees.SelectMany(tree => tree.Processes).SelectMany(process => process.Ports).ToImmutableHashSet();
        var targets = listening.Order()
            .Select(port => new PreviewTarget(job, Address(port), PreviewSource.Server) { Leased = lease.Match(block => block.Holds(port), () => false) })
            .ToImmutableList();

        return new Sighting(RealFiles.Folder(worktree), lease, listening, targets);
    }
}
