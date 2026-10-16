using Avala.Agents.Contracts.Events;
using Avala.Permissions.Policies;

namespace Avala.Permissions.Answering;

internal sealed class RequestPlaces(IRealPaths paths, IReadableFolders readable)
{
    public async ValueTask<PermissionRequest> FactsAsync(GovernedSession session, PermissionRequested requested, CancellationToken cancellationToken)
    {
        var folders = requested.Reads.IsSome || requested.Kind == ItemKind.Command ? await readable.FoldersAsync(cancellationToken) : [];

        return requested.Facts(session.WorkingDirectory, session.Scratch, folders, paths);
    }
}
