using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Folders;

internal sealed class FolderCoverage(GovernanceBook book, IRealPaths paths, IReadableFolders readable, TimeProvider clock)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public Task<GovernedSession> GovernedAsync(SessionId session, CancellationToken cancellationToken) =>
        book.WorkingAsync(session, Patience, clock, cancellationToken);

    public FolderRequest Real(FolderRequest request) =>
        request with { Folder = paths.Resolve(request.Folder).Match(real => real, () => request.Folder) };

    public async Task<Option<ToolResult>> AnsweredAsync(GovernedSession governed, ToolCalled called, FolderRequest request, CancellationToken cancellationToken)
    {
        if (governed.Job.IsNone)
        {
            return FolderTool.NoJob(called.Item);
        }

        if (governed.WorkingDirectory.Match(root => RequestFacts.Within(request.Folder, [root], paths), () => false))
        {
            return FolderTool.Yours(called.Item, request.Folder);
        }

        if (!Directory.Exists(request.Folder))
        {
            return FolderTool.Missing(called.Item, request.Folder);
        }

        return await CoveredAsync(governed, request, cancellationToken) ? FolderTool.Already(called.Item, request) : Option<ToolResult>.None;
    }

    private async Task<bool> CoveredAsync(GovernedSession governed, FolderRequest request, CancellationToken cancellationToken)
    {
        var rules = book.RulesOf(governed);
        var editable = RequestPlaces.Grants(rules, ItemKind.FileEdit);

        return request.Access == FolderAccess.Edit
            ? RequestFacts.Within(request.Folder, editable, paths)
            : RequestFacts.Within(request.Folder, [.. await readable.FoldersAsync(cancellationToken), .. RequestPlaces.Grants(rules, Option<ItemKind>.None)], paths);
    }
}
