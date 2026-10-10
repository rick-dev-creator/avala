using Avala.Sdk;

namespace Avala.Forges.Contracts;

public sealed record PullRequestDraft(string Head, string Base, string Title, string Body);

public sealed record PullRequestRef(int Number, Uri Link, string Head, string Base);

public enum PullRequestLifecycle
{
    Open,
    Merged,
    Closed,
}

public enum Mergeability
{
    Unknown,
    Mergeable,
    Conflicting,
}

public enum CheckStatus
{
    Pending,
    Passed,
    Failed,
    Skipped,
}

public sealed record CheckRun(string Name, CheckStatus Status, Option<Uri> Link, string Summary);

public enum ReviewVerdict
{
    Approved,
    ChangesRequested,
    Commented,
}

public sealed record ReviewComment(string Path, Option<int> Line, string Body);

public sealed record Review(string Id, string Author, ReviewVerdict Verdict, string Body, string Commit)
{
    public IReadOnlyList<ReviewComment> Comments { get; init; } = [];
}

public sealed record PullRequestState(PullRequestRef PullRequest, PullRequestLifecycle Lifecycle, string HeadCommit, Mergeability Mergeability)
{
    public IReadOnlyList<CheckRun> Checks { get; init; } = [];

    public IReadOnlyList<Review> Reviews { get; init; } = [];
}

public sealed record CommentRef(string Id, Option<Uri> Link);

public enum ForgeError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidName,
    DuplicateName,
    MissingForge,
    InvalidUrl,
    UnknownSource,
    MissingReference,
    InvalidInterval,
    UnknownPolicy,
    InvalidWakeUps,
    UnknownConnection,
    UnknownForge,
    MissingUrl,
    MissingCredential,
    CliUnavailable,
    Unauthorized,
    NotFound,
    RateLimited,
    Rejected,
    Unreachable,
    InvalidRemote,
    NoBaseBranch,
    PushRejected,
    GitFailed,
}
