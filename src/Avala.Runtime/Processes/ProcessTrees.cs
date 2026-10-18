using System.Collections.Immutable;
using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessTrees(IContainment containment, IEnumerable<IProcessEnvironment> environments, TimeProvider clock, TreeLedger ledger) : IProcessTrees, IAsyncDisposable
{
    private ImmutableList<ProcessTree> open = [];

    public IReadOnlyList<IProcessTree> Open => Volatile.Read(ref open);

    public async ValueTask<IProcessTree> OpenAsync(string home, CancellationToken cancellationToken)
    {
        var id = ProcessTreeId.New();
        var environment = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var contributor in environments)
        {
            environment.AddRange(await contributor.ForAsync(home, cancellationToken));
        }

        environment[ProcessTreeId.Variable] = id.Value.ToString();
        var tree = new ProcessTree(id, home, environment.ToImmutable(), await containment.CreateAsync(id, cancellationToken));
        ImmutableInterlocked.Update(ref open, trees => trees.Add(tree));
        await ledger.KeepAsync(OpenIds, cancellationToken);

        return tree;
    }

    public async Task<IReadOnlyList<(ProcessTreeId Tree, int Reaped)>> ReapEarlierAsync(CancellationToken cancellationToken)
    {
        var reaped = new List<(ProcessTreeId, int)>();

        foreach (var earlier in await ledger.EarlierAsync(cancellationToken))
        {
            var tree = new ProcessTree(earlier, string.Empty, ImmutableDictionary<string, string>.Empty, containment.Recover(earlier));

            try
            {
                var members = await tree.MembersAsync(cancellationToken);
                _ = await tree.KillAsync(clock, cancellationToken);
                reaped.Add((earlier, members.Count));
            }
            finally
            {
                tree.Release();
            }

            await ledger.ReapedAsync(earlier, OpenIds, cancellationToken);
        }

        return reaped;
    }

    private IEnumerable<ProcessTreeId> OpenIds() => Volatile.Read(ref open).Select(tree => tree.Id);

    public Option<IProcessTree> In(string folder) =>
        Volatile.Read(ref open).LastOrDefault(tree => Folders.Same(tree.Home, folder)).ToOption().Map(tree => (IProcessTree)tree);

    public Option<IProcessTree> Find(ProcessTreeId tree) =>
        Volatile.Read(ref open).FirstOrDefault(known => known.Id == tree).ToOption().Map(known => (IProcessTree)known);

    public async ValueTask<IReadOnlyList<IReadOnlyList<TreeProcess>>> MembersAsync(IReadOnlyList<IProcessTree> trees, CancellationToken cancellationToken) =>
        [.. (await containment.MemberIdsAsync([.. trees.Cast<ProcessTree>().Select(tree => tree.Container)], cancellationToken)).Select(ProcessTree.Described)];

    public async ValueTask<IReadOnlyList<TreeProcess>> CloseAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        var closing = Volatile.Read(ref open).FirstOrDefault(known => known.Id == tree);

        if (closing is null || !ImmutableInterlocked.Update(ref open, trees => trees.Remove(closing)))
        {
            return [];
        }

        try
        {
            return await closing.KillAsync(clock, cancellationToken);
        }
        finally
        {
            closing.Release();
            await ledger.KeepAsync(OpenIds, CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tree in Volatile.Read(ref open))
        {
            _ = await CloseAsync(tree.Id, CancellationToken.None);
        }
    }
}

internal static class Folders
{
    public static bool Same(string one, string other) =>
        !string.IsNullOrWhiteSpace(one)
        && !string.IsNullOrWhiteSpace(other)
        && string.Equals(Normalized(one), Normalized(other), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalized(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
