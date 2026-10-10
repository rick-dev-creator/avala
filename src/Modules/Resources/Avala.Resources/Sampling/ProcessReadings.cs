using System.Collections.Immutable;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Resources.Sampling;

internal sealed class ProcessReadings(IProcessTrees trees, IListeningPorts listening)
{
    public Option<IProcessTree> Find(ProcessTreeId tree) => trees.Find(tree);

    public ValueTask<IReadOnlyList<TreeProcess>> CloseAsync(ProcessTreeId tree, CancellationToken cancellationToken) =>
        trees.CloseAsync(tree, cancellationToken);

    public async Task<IReadOnlyList<ProcessUsage>> ReadAsync(IProcessTree tree, CancellationToken cancellationToken)
    {
        var (read, listeners) = await ReadAsync([tree], cancellationToken);

        return Usage(read[0].Members, listeners);
    }

    public Task<(IReadOnlyList<(IProcessTree Tree, IReadOnlyList<TreeProcess> Members)> Trees, IReadOnlyList<Listener> Listeners)> ReadOpenAsync(
        CancellationToken cancellationToken) =>
        ReadAsync(trees.Open, cancellationToken);

    public static IReadOnlyList<ProcessUsage> Usage(IReadOnlyList<TreeProcess> members, IReadOnlyList<Listener> listeners) =>
    [
        .. members.Select(member => new ProcessUsage(
            member.Id,
            member.Name,
            member.MemoryBytes,
            member.CpuTime,
            [.. listeners.Where(listener => listener.Process == Option<int>.Some(member.Id)).Select(listener => listener.Port).Distinct().Order()])),
    ];

    private async Task<(IReadOnlyList<(IProcessTree Tree, IReadOnlyList<TreeProcess> Members)> Trees, IReadOnlyList<Listener> Listeners)> ReadAsync(
        IReadOnlyList<IProcessTree> which,
        CancellationToken cancellationToken)
    {
        var read = new List<(IProcessTree Tree, IReadOnlyList<TreeProcess> Members)>();

        foreach (var tree in which)
        {
            read.Add((tree, await tree.MembersAsync(cancellationToken)));
        }

        var owners = read.SelectMany(tree => tree.Members).Select(member => member.Id).Append(Environment.ProcessId).ToImmutableHashSet();

        return (read, await listening.ListAsync(owners, cancellationToken));
    }
}
