using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class MacContainment : IContainment
{
    public ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IContainer>(new MacContainer(tree));
}

internal sealed class MacContainer(ProcessTreeId tree) : IContainer
{
    private readonly string marker = $"{ProcessTreeId.Variable}={tree.Value}";
    private ImmutableHashSet<int> roots = [];

    public ProcessStartInfo Prepare(ProcessStartInfo info) => info;

    public void Adopt(Process process) => ImmutableInterlocked.Update(ref roots, known => known.Add(process.Id));

    public async ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken)
    {
        var listed = await Commands.OutputAsync("ps", ["-axEww", "-o", "pid=,ppid=,stat=,command="], cancellationToken);
        var candidates = listed
            .Match(output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries), () => [])
            .Select(line => line.Trim().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 4 && !fields[2].StartsWith('Z'))
            .Select(fields => (
                Id: int.Parse(fields[0], CultureInfo.InvariantCulture),
                Parent: int.Parse(fields[1], CultureInfo.InvariantCulture),
                Marked: fields[3].Contains(marker, StringComparison.Ordinal)))
            .Where(candidate => candidate.Id != Environment.ProcessId)
            .ToList();
        var known = Volatile.Read(ref roots);
        var members = candidates.Where(candidate => candidate.Marked || known.Contains(candidate.Id)).Select(candidate => candidate.Id).ToHashSet();

        while (candidates.Where(candidate => !members.Contains(candidate.Id) && members.Contains(candidate.Parent)).ToList() is { Count: > 0 } descendants)
        {
            members.UnionWith(descendants.Select(descendant => descendant.Id));
        }

        return [.. members];
    }

    public void Dispose()
    {
    }
}
