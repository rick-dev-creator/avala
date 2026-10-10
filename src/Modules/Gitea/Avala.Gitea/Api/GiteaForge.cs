using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Gitea.Api;

internal sealed record GiteaDialect(string Id, string Name, string Cli)
{
    public static GiteaDialect Gitea { get; } = new("gitea", "Gitea", "tea");

    public static GiteaDialect Forgejo { get; } = new("forgejo", "Forgejo", "tea");
}

internal sealed class GiteaForge(GiteaDialect dialect) : IForge
{
    public const string ApiRoot = "/api/v1";

    public ForgeInfo Info { get; } = new(dialect.Id, dialect.Name) { TokenScheme = "token" };

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target)
    {
        List<string> arguments = ["api", "--method", request.Method == ForgeMethod.Post ? "POST" : "GET", "--include"];

        foreach (var (name, value) in request.Fields)
        {
            arguments.AddRange(["--field", $"{name}={value}"]);
        }

        arguments.Add(request.Path.StartsWith(ApiRoot, StringComparison.Ordinal) ? request.Path[ApiRoot.Length..].TrimStart('/') : request.Path.TrimStart('/'));

        return new CliCall(dialect.Cli, arguments, CliStatusChannel.Error);
    }

    public async ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
        (await GetAsync(context, $"{Repository(context)}/pulls?state=open", cancellationToken))
            .Bind(body => Json.Read(body, root => root.EnumerateArray()
                .Where(pull => Json.Text(pull.GetProperty("head"), "ref") == head)
                .Select(Reference)
                .FirstOrDefault()
                .ToOption()));

    public async ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(
            new ForgeRequest(ForgeMethod.Post, $"{Repository(context)}/pulls")
            {
                Fields = [new("title", draft.Title), new("head", draft.Head), new("base", draft.Base), new("body", draft.Body)],
            },
            cancellationToken))
            .Bind(response => Json.Read(response.Body, Reference));

    public async ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken)
    {
        var path = $"{Repository(context)}/pulls/{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}";

        if (!(await GetAsync(context, path, cancellationToken)).Bind(body => Json.Read(body, root => Pull(root, pullRequest))).TryGetValue(out var pull, out var error)
            || !(await GetAsync(context, $"{Repository(context)}/commits/{Uri.EscapeDataString(pull.HeadCommit)}/status", cancellationToken)).Bind(body => Json.Read(body, Statuses)).TryGetValue(out var statuses, out error)
            || !(await GetAsync(context, $"{path}/reviews", cancellationToken)).Bind(body => Json.Read(body, Reviews)).TryGetValue(out var reviews, out error))
        {
            return error;
        }

        var commented = new List<Review>();

        foreach (var review in reviews)
        {
            if (review.Verdict != ReviewVerdict.ChangesRequested)
            {
                commented.Add(review);
                continue;
            }

            if (!(await GetAsync(context, $"{path}/reviews/{Uri.EscapeDataString(review.Id)}/comments", cancellationToken)).Bind(body => Json.Read(body, Comments)).TryGetValue(out var comments, out error))
            {
                return error;
            }

            commented.Add(review with { Comments = comments });
        }

        return pull with { Checks = statuses, Reviews = commented };
    }

    public async ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(
            new ForgeRequest(ForgeMethod.Post, $"{Repository(context)}/issues/{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}/comments") { Fields = [new("body", body)] },
            cancellationToken))
            .Bind(response => Json.Read(response.Body, root => new CommentRef(Json.Id(root), Json.Link(root, "html_url"))));

    public static CheckStatus CommitStatus(string status) => status switch
    {
        "success" => CheckStatus.Passed,
        "pending" => CheckStatus.Pending,
        "skipped" => CheckStatus.Skipped,
        _ => CheckStatus.Failed,
    };

    private static async Task<Result<string, ForgeError>> GetAsync(ForgeContext context, string path, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, path), cancellationToken)).Map(response => response.Body);

    private static string Repository(ForgeContext context) =>
        $"{ApiRoot}/repos/{Uri.EscapeDataString(context.Target.Repository.Owner)}/{Uri.EscapeDataString(context.Target.Repository.Name)}";

    private static PullRequestRef Reference(JsonElement pull) =>
        new(pull.GetProperty("number").GetInt32(), new Uri(pull.GetProperty("html_url").GetString()!), Json.Text(pull.GetProperty("head"), "ref"), Json.Text(pull.GetProperty("base"), "ref"));

    private static PullRequestState Pull(JsonElement pull, PullRequestRef known)
    {
        var lifecycle = pull.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True ? PullRequestLifecycle.Merged
            : Json.Text(pull, "state") == "closed" ? PullRequestLifecycle.Closed
            : PullRequestLifecycle.Open;
        var mergeability = pull.TryGetProperty("mergeable", out var mergeable) ? mergeable.ValueKind switch
        {
            JsonValueKind.True => Mergeability.Mergeable,
            JsonValueKind.False => Mergeability.Conflicting,
            _ => Mergeability.Unknown,
        } : Mergeability.Unknown;

        return new PullRequestState(known with { Link = new Uri(pull.GetProperty("html_url").GetString()!) }, lifecycle, Json.Text(pull.GetProperty("head"), "sha"), mergeability);
    }

    private static IReadOnlyList<CheckRun> Statuses(JsonElement root) =>
        root.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array
            ? [.. statuses.EnumerateArray().Select(status => new CheckRun(Json.Text(status, "context"), CommitStatus(Json.Text(status, "status")), Json.Link(status, "target_url"), Json.Text(status, "description")))]
            : [];

    private static IReadOnlyList<Review> Reviews(JsonElement root) =>
        [
            .. root.EnumerateArray()
                .Where(review => !(review.TryGetProperty("dismissed", out var dismissed) && dismissed.ValueKind == JsonValueKind.True))
                .Select(review => (Review: review, Verdict: Json.Text(review, "state") switch
                {
                    "APPROVED" => Option<ReviewVerdict>.Some(ReviewVerdict.Approved),
                    "REQUEST_CHANGES" => ReviewVerdict.ChangesRequested,
                    "COMMENT" => ReviewVerdict.Commented,
                    _ => Option<ReviewVerdict>.None,
                }))
                .Where(found => found.Verdict.IsSome)
                .Select(found => new Review(
                    Json.Id(found.Review),
                    Json.Text(found.Review.GetProperty("user"), "login"),
                    found.Verdict.Match(verdict => verdict, () => ReviewVerdict.Commented),
                    Json.Text(found.Review, "body"),
                    Json.Text(found.Review, "commit_id"))),
        ];

    private static IReadOnlyList<ReviewComment> Comments(JsonElement root) =>
        [.. root.EnumerateArray().Select(comment => new ReviewComment(Json.Text(comment, "path"), Json.Number(comment, "position"), Json.Text(comment, "body")))];
}
