using System.Diagnostics;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal interface IContainment
{
    ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<IReadOnlyList<int>>> MemberIdsAsync(IReadOnlyList<IContainer> containers, CancellationToken cancellationToken);
}

internal static class Containers
{
    public static async ValueTask<IReadOnlyList<IReadOnlyList<int>>> EachMemberIdsAsync(IReadOnlyList<IContainer> containers, CancellationToken cancellationToken)
    {
        var members = new List<IReadOnlyList<int>>(containers.Count);

        foreach (var container in containers)
        {
            members.Add(await container.MemberIdsAsync(cancellationToken));
        }

        return members;
    }
}

internal interface IContainer : IDisposable
{
    ProcessStartInfo Prepare(ProcessStartInfo info);

    void Adopt(Process process);

    ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken);
}
