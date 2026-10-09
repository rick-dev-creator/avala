using System.Collections.Immutable;
using System.Diagnostics;
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

    public async ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken) =>
        MacListings.Members(
            await Commands.OutputAsync("ps", MacListings.ProcessArguments, cancellationToken),
            marker,
            Volatile.Read(ref roots),
            Environment.ProcessId);

    public void Dispose()
    {
    }
}
