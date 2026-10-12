using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Publishing;

internal sealed class RepositoryPublisher(ForgeConnections connections, IRepositories repositories, IRemoteRepositories remotes) : IRepositoryPublishing
{
    public const string Origin = "origin";

    public async ValueTask<IReadOnlyList<PublishTarget>> TargetsAsync(CancellationToken cancellationToken) =>
        [.. (await connections.AllUsableAsync(cancellationToken)).Where(usable => usable.Forge is IForgeRepositories).Select(Target)];

    public async ValueTask<Option<RepositoryRemote>> RemoteOfAsync(string repository, CancellationToken cancellationToken)
    {
        var url = await remotes.RemoteAsync(repository, Origin, cancellationToken);

        if (url.IsNone)
        {
            return Option<RepositoryRemote>.None;
        }

        var usable = await connections.AllUsableAsync(cancellationToken);

        return url.Map(found => Described(found, usable));
    }

    private static RepositoryRemote Described(string url, IReadOnlyList<UsableForge> usable)
    {
        var address = RemoteAddresses.Parse(url);
        var forge = address.Bind(found => usable
            .Where(candidate => candidate.Forge is IForgeRepositories hosting && string.Equals(hosting.GitHost(candidate.Url), found.Host, StringComparison.OrdinalIgnoreCase))
            .Select(Target)
            .FirstOrDefault()
            .ToOption());

        return new RepositoryRemote(Origin, url) { Address = address, Forge = forge };
    }

    public async ValueTask<Result<PublishedRepository, ForgeError>> PublishAsync(string repository, ForgeName forge, RepositoryDraft draft, CancellationToken cancellationToken)
    {
        if (!(await connections.AccountAsync(forge, cancellationToken)).TryGetValue(out var account, out var error))
        {
            return error;
        }

        if (account.Forge is not IForgeRepositories hosting)
        {
            return ForgeError.RepositoriesUnsupported;
        }

        if (await repositories.InspectAsync(repository, cancellationToken) != RepositoryState.Ready)
        {
            return ForgeError.NoBaseBranch;
        }

        var target = new PublishTarget(forge, account.Forge.Info.Name);
        var known = await RemoteOfAsync(repository, cancellationToken);
        var destination = await known.Match(
            remote => Task.FromResult(Resumed(remote, target, draft)),
            () => CreatedAsync(repository, hosting, account.Context, draft, cancellationToken));

        if (!destination.TryGetValue(out var found, out error))
        {
            return error;
        }

        return (await remotes.PublishAsync(repository, Origin, found.Url, cancellationToken)).Match(
            branch => Result<PublishedRepository, ForgeError>.Success(new PublishedRepository(target, found.Address, branch, found.Url) { Link = found.Link }),
            failure => PublishFailures.Of(failure));
    }

    private static PublishTarget Target(UsableForge usable) => new(usable.Name, usable.Forge.Info.Name);

    private static Result<Destination, ForgeError> Resumed(RepositoryRemote remote, PublishTarget target, RepositoryDraft draft) =>
        remote.Forge == target && remote.Address.Match(address => address.Name == draft.Name.Trim(), () => false)
            ? new Destination(remote.Address.Match(address => address, () => new RepositoryAddress(string.Empty, string.Empty, draft.Name)), remote.Url, Option<Uri>.None)
            : ForgeError.RemoteExists;

    private async Task<Result<Destination, ForgeError>> CreatedAsync(string repository, IForgeRepositories hosting, ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken)
    {
        if (!(await hosting.CreateAsync(context, draft with { Name = draft.Name.Trim() }, cancellationToken)).TryGetValue(out var hosted, out var error))
        {
            return error;
        }

        var https = hosted.SshUrl.Length == 0 || await remotes.HasCredentialHelperAsync(repository, hosted.HttpsUrl, cancellationToken);

        return new Destination(hosted.Address, https ? hosted.HttpsUrl : hosted.SshUrl, hosted.Link);
    }

    private sealed record Destination(RepositoryAddress Address, string Url, Option<Uri> Link);
}

internal static class PublishFailures
{
    public static ForgeError Of(WorkspaceFailure failure) => failure switch
    {
        WorkspaceFailure.RemoteUnauthorized => ForgeError.PushUnauthorized,
        WorkspaceFailure.RemoteNotFound => ForgeError.NotFound,
        WorkspaceFailure.RemoteUnreachable => ForgeError.Unreachable,
        WorkspaceFailure.PushRejected => ForgeError.PushRejected,
        WorkspaceFailure.RemoteExists => ForgeError.RemoteExists,
        WorkspaceFailure.NoBaseBranch => ForgeError.NoBaseBranch,
        WorkspaceFailure.InvalidRemoteUrl => ForgeError.InvalidRemote,
        _ => ForgeError.GitFailed,
    };
}
