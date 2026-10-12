using Avala.Sdk;

namespace Avala.Forges.Contracts;

public sealed record RepositoryDraft(string Name, bool Private);

public sealed record HostedRepository(RepositoryAddress Address, Uri Link, string HttpsUrl, string SshUrl);

public sealed record ListedRepository(HostedRepository Repository, bool Private, bool Archived, DateTimeOffset UpdatedAt);

public interface IForgeRepositories
{
    string GitHost(Uri api);

    ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken);
}

public interface IForgeAccounts
{
    ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken);
}

public sealed record RemoteRepository(RepositoryAddress Address, Uri Link, bool Private, bool Archived, DateTimeOffset UpdatedAt, string CloneUrl);

public sealed record PublishTarget(ForgeName Forge, string ForgeKind);

public sealed record RepositoryRemote(string Remote, string Url)
{
    public Option<RepositoryAddress> Address { get; init; }

    public Option<PublishTarget> Forge { get; init; }
}

public sealed record PublishedRepository(PublishTarget Forge, RepositoryAddress Repository, string Branch, string PushUrl)
{
    public Option<Uri> Link { get; init; }
}

public interface IRepositoryPublishing
{
    ValueTask<IReadOnlyList<PublishTarget>> TargetsAsync(CancellationToken cancellationToken);

    ValueTask<Option<RepositoryRemote>> RemoteOfAsync(string repository, CancellationToken cancellationToken);

    ValueTask<Result<PublishedRepository, ForgeError>> PublishAsync(string repository, ForgeName forge, RepositoryDraft draft, CancellationToken cancellationToken);
}
