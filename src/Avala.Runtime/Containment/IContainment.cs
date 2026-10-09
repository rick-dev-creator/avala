using System.Diagnostics;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal interface IContainment
{
    ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken);
}

internal interface IContainer : IDisposable
{
    ProcessStartInfo Prepare(ProcessStartInfo info);

    void Adopt(Process process);

    ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken);
}
