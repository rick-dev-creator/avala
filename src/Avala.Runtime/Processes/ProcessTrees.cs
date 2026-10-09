using System.Collections.Immutable;
using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessTrees(IContainment containment, IEnumerable<IProcessEnvironment> environments, TimeProvider clock) : IProcessTrees, IAsyncDisposable
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

        return tree;
    }

    public Option<IProcessTree> In(string folder) =>
        Volatile.Read(ref open).LastOrDefault(tree => Folders.Same(tree.Home, folder)).ToOption().Map(tree => (IProcessTree)tree);

    public Option<IProcessTree> Find(ProcessTreeId tree) =>
        Volatile.Read(ref open).FirstOrDefault(known => known.Id == tree).ToOption().Map(known => (IProcessTree)known);

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
