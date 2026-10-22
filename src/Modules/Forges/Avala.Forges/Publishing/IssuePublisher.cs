using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Publishing;

internal sealed class IssuePublisher(IRepositoryPublishing publishing, ForgeConnections connections) : IIssuePublishing
{
    public async ValueTask<Result<IssueRef, ForgeError>> PublishAsync(string repository, IssueDraft draft, CancellationToken cancellationToken)
    {
        var remote = (await publishing.RemoteOfAsync(repository, cancellationToken)).ToResult(ForgeError.MissingForge);

        if (!remote.TryGetValue(out var known, out var error)
            || !known.Forge.ToResult(ForgeError.MissingForge).TryGetValue(out var target, out error)
            || !(await connections.ResolveAsync(target.Forge, known.Url, cancellationToken)).TryGetValue(out var resolved, out error))
        {
            return error;
        }

        return resolved.Forge is IForgeIssues issues
            ? await issues.OpenIssueAsync(resolved.Context, draft, cancellationToken)
            : ForgeError.IssuesUnsupported;
    }
}
