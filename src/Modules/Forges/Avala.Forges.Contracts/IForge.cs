using Avala.Sdk;

namespace Avala.Forges.Contracts;

public interface IForge
{
    ForgeInfo Info { get; }

    Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target);

    ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken);

    ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken);

    ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken);

    ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken);
}

public interface IForgeApi
{
    ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken);
}

public sealed record ForgeInfo(string Id, string Name)
{
    public Option<Uri> DefaultUrl { get; init; }

    public string TokenScheme { get; init; } = "Bearer";
}

public sealed record RepositoryAddress(string Host, string Owner, string Name);

public sealed record ForgeTarget(Uri Api, RepositoryAddress Repository, string Remote);

public sealed record ForgeContext(IForgeApi Api, ForgeTarget Target);

public enum ForgeMethod
{
    Get,
    Post,
}

public sealed record ForgeRequest(ForgeMethod Method, string Path)
{
    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; init; } = [];
}

public sealed record ForgeResponse(int Status, string Body);

public enum CliStatusChannel
{
    Output,
    Error,
}

public sealed record CliCall(string Command, IReadOnlyList<string> Arguments, CliStatusChannel Status);
