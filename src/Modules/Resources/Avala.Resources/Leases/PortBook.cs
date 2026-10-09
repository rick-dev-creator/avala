using System.Collections.Immutable;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Leases;

internal sealed record PortBook(PortRange Range, ImmutableList<PortLease> Leases)
{
    public const string FirstPort = "AVALA_PORT";

    public const string AllPorts = "AVALA_PORTS";

    public static PortBook Open(PortRange range) => new(range, []);

    public Option<PortLease> Of(string worktree) => Leases.FirstOrDefault(lease => lease.Worktree == worktree).ToOption();

    public (PortBook Book, Option<PortLease> Lease) Lease(string worktree, IReadOnlySet<int> busy)
    {
        if (Of(worktree).IsSome)
        {
            return (this, Of(worktree));
        }

        var free = Blocks().FirstOrDefault(block =>
            !Leases.Any(lease => lease.First == block.First)
            && !Enumerable.Range(block.First, block.Last - block.First + 1).Any(busy.Contains));

        return free is null
            ? (this, Option<PortLease>.None)
            : (this with { Leases = Leases.Add(free with { Worktree = worktree }) }, free with { Worktree = worktree });
    }

    public (PortBook Book, Option<PortLease> Released) Release(string worktree) =>
        Of(worktree).Match(
            lease => (this with { Leases = Leases.Remove(lease) }, Option<PortLease>.Some(lease)),
            () => (this, Option<PortLease>.None));

    public static IReadOnlyList<(int Port, PortLease Lease, Option<int> Process)> Conflicts(
        IReadOnlyList<PortLease> leases,
        IEnumerable<(int Port, Option<int> Process)> listening,
        IReadOnlyDictionary<int, string> homes) =>
    [
        .. from listener in listening
           from lease in leases
           where listener.Port >= lease.First && listener.Port <= lease.Last
           where !listener.Process.Match(process => homes.TryGetValue(process, out var home) && home == lease.Worktree, () => false)
           select (listener.Port, lease, listener.Process),
    ];

    public static IReadOnlyDictionary<string, string> Variables(PortLease lease) => new Dictionary<string, string>
    {
        [FirstPort] = lease.First.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [AllPorts] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lease.First}-{lease.Last}"),
    };

    private IEnumerable<PortLease> Blocks()
    {
        for (var first = Range.First; first + Range.PerWorktree - 1 <= Range.Last; first += Range.PerWorktree)
        {
            yield return new PortLease(string.Empty, first, first + Range.PerWorktree - 1);
        }
    }
}
