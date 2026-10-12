using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.GitHub.Api;

internal sealed class GitHubForge : IForge, IForgeEvents, IForgeRepositories, IForgeAccounts
{
    public const string Id = "github";

    public static Uri Public { get; } = new("https://api.github.com");

    public const string Mark = "M8 0C12.42 0 16 3.58 16 8A8.01 8.01 0 0 1 10.55 15.59C10.15 15.67 10 15.42 10 15.21C10 14.94 10.01 14.08 10.01 13.01C10.01 12.26 9.76 11.78 9.47 11.53C11.25 11.33 13.12 10.65 13.12 7.58C13.12 6.7 12.81 5.99 12.3 5.43C12.38 5.23 12.66 4.41 12.22 3.31C12.22 3.31 11.55 3.09 10.02 4.13C9.38 3.95 8.7 3.86 8.02 3.86C7.34 3.86 6.66 3.95 6.02 4.13C4.49 3.1 3.82 3.31 3.82 3.31C3.38 4.41 3.66 5.23 3.74 5.43C3.23 5.99 2.92 6.71 2.92 7.58C2.92 10.64 4.78 11.33 6.56 11.53C6.33 11.73 6.12 12.08 6.05 12.6C5.59 12.81 4.44 13.15 3.72 11.94C3.57 11.7 3.12 11.11 2.49 11.12C1.82 11.13 2.22 11.5 2.5 11.65C2.84 11.84 3.23 12.55 3.32 12.78C3.48 13.23 4 14.09 6.01 13.72C6.01 14.39 6.02 15.02 6.02 15.21C6.02 15.42 5.87 15.66 5.47 15.59A8 8 0 0 1 0 8C0 3.58 3.58 0 8 0Z";

    public ForgeInfo Info { get; } = new(Id, "GitHub") { DefaultUrl = Public, TokenScheme = "Bearer", Logo = Mark };

    public HookSignature Signature { get; } = new(["X-Hub-Signature-256"], "sha256=");

    public async ValueTask<Result<ForgeEventPage, ForgeError>> ReadSinceAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken) =>
        await GitHubEvents.ReadAsync(context, query, cancellationToken);

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target)
    {
        List<string> arguments = ["api", "--method", request.Method == ForgeMethod.Post ? "POST" : "GET", "--include", "-H", "Accept: application/vnd.github+json"];

        var hostname = target.Login.Match(login => login, () => target.Api.Host != Public.Host ? target.Api.Host : string.Empty);

        if (hostname.Length > 0)
        {
            arguments.AddRange(["--hostname", hostname]);
        }

        foreach (var (name, value) in request.Fields)
        {
            arguments.AddRange(["-f", $"{name}={value}"]);
        }

        foreach (var (name, value) in request.Flags)
        {
            arguments.AddRange(["-F", $"{name}={(value ? "true" : "false")}"]);
        }

        arguments.Add(request.Path.TrimStart('/'));

        return new CliCall("gh", arguments, CliStatusChannel.Output);
    }

    public string GitHost(Uri api) => GitHubRepositories.GitHost(api);

    public async ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) =>
        await GitHubRepositories.CreateAsync(context, draft, cancellationToken);

    public async ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        await GitHubRepositories.ListAsync(context, cancellationToken);

    public async ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) =>
        await GitHubRepositories.MeAsync(context, cancellationToken);

    public async ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
        (await GetAsync(context, $"{Repository(context)}/pulls?state=open&head={Escape(context.Target.Repository.Owner)}:{Escape(head)}", cancellationToken))
            .Bind(body => Json.Read(body, root => root.EnumerateArray().Select(Reference).FirstOrDefault().ToOption()));

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

        if (!(await GetAsync(context, path, cancellationToken)).Bind(body => Json.Read(body, root => Pull(root, pullRequest))).TryGetValue(out var pull, out var error))
        {
            return error;
        }

        var commit = Escape(pull.HeadCommit);

        if (!(await GetAsync(context, $"{Repository(context)}/commits/{commit}/check-runs", cancellationToken)).Bind(body => Json.Read(body, CheckRuns)).TryGetValue(out var runs, out error)
            || !(await GetAsync(context, $"{Repository(context)}/commits/{commit}/status", cancellationToken)).Bind(body => Json.Read(body, Statuses)).TryGetValue(out var statuses, out error)
            || !(await GetAsync(context, $"{path}/reviews", cancellationToken)).Bind(body => Json.Read(body, Reviews)).TryGetValue(out var reviews, out error)
            || !(await GetAsync(context, $"{path}/comments", cancellationToken)).Bind(body => Json.Read(body, Comments)).TryGetValue(out var comments, out error))
        {
            return error;
        }

        return pull with
        {
            Checks = [.. runs, .. statuses],
            Reviews = [.. reviews.Select(review => review with { Comments = comments.GetValueOrDefault(review.Id, []) })],
        };
    }

    public async ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(
            new ForgeRequest(ForgeMethod.Post, $"{Repository(context)}/issues/{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}/comments") { Fields = [new("body", body)] },
            cancellationToken))
            .Bind(response => Json.Read(response.Body, root => new CommentRef(Json.Id(root), Json.Link(root, "html_url"))));

    public static CheckStatus RunStatus(string status, Option<string> conclusion) =>
        status != "completed" ? CheckStatus.Pending
        : conclusion.Match(found => found, () => string.Empty) switch
        {
            "success" or "neutral" => CheckStatus.Passed,
            "skipped" => CheckStatus.Skipped,
            _ => CheckStatus.Failed,
        };

    public static CheckStatus CommitStatus(string state) => state switch
    {
        "success" => CheckStatus.Passed,
        "pending" => CheckStatus.Pending,
        _ => CheckStatus.Failed,
    };

    private static async Task<Result<string, ForgeError>> GetAsync(ForgeContext context, string path, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, path), cancellationToken)).Map(response => response.Body);

    private static string Repository(ForgeContext context) =>
        $"/repos/{Escape(context.Target.Repository.Owner)}/{Escape(context.Target.Repository.Name)}";

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static PullRequestRef Reference(JsonElement pull) =>
        new(pull.GetProperty("number").GetInt32(), new Uri(pull.GetProperty("html_url").GetString()!), Json.Text(pull.GetProperty("head"), "ref"), Json.Text(pull.GetProperty("base"), "ref"));

    private static PullRequestState Pull(JsonElement pull, PullRequestRef known)
    {
        var lifecycle = pull.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True ? PullRequestLifecycle.Merged
            : Json.Text(pull, "state") == "closed" ? PullRequestLifecycle.Closed
            : PullRequestLifecycle.Open;
        var mergeable = pull.TryGetProperty("mergeable", out var flag) ? flag.ValueKind : JsonValueKind.Null;
        var mergeability = Json.Text(pull, "mergeable_state") == "dirty" || mergeable == JsonValueKind.False ? Mergeability.Conflicting
            : mergeable == JsonValueKind.True ? Mergeability.Mergeable
            : Mergeability.Unknown;

        return new PullRequestState(known with { Link = new Uri(pull.GetProperty("html_url").GetString()!) }, lifecycle, Json.Text(pull.GetProperty("head"), "sha"), mergeability);
    }

    public static IReadOnlyList<CheckRun> CheckRuns(JsonElement root) =>
        [
            .. root.GetProperty("check_runs").EnumerateArray().Select(run => new CheckRun(
                Json.Text(run, "name"),
                RunStatus(Json.Text(run, "status"), run.TryGetProperty("conclusion", out var conclusion) && conclusion.ValueKind == JsonValueKind.String ? Json.Text(run, "conclusion") : Option<string>.None),
                Json.Link(run, "html_url"),
                run.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object ? Json.Text(output, "title") : string.Empty)),
        ];

    public static IReadOnlyList<CheckRun> Statuses(JsonElement root) =>
        [.. root.GetProperty("statuses").EnumerateArray().Select(status => new CheckRun(Json.Text(status, "context"), CommitStatus(Json.Text(status, "state")), Json.Link(status, "target_url"), Json.Text(status, "description")))];

    private static IReadOnlyList<Review> Reviews(JsonElement root) =>
        [
            .. root.EnumerateArray()
                .Select(review => (Review: review, Verdict: Json.Text(review, "state") switch
                {
                    "APPROVED" => Option<ReviewVerdict>.Some(ReviewVerdict.Approved),
                    "CHANGES_REQUESTED" => ReviewVerdict.ChangesRequested,
                    "COMMENTED" => ReviewVerdict.Commented,
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

    private static Dictionary<string, IReadOnlyList<ReviewComment>> Comments(JsonElement root) =>
        root.EnumerateArray()
            .Where(comment => comment.TryGetProperty("pull_request_review_id", out var review) && review.ValueKind == JsonValueKind.Number)
            .GroupBy(comment => comment.GetProperty("pull_request_review_id").GetInt64().ToString(CultureInfo.InvariantCulture))
            .ToDictionary(
                byReview => byReview.Key,
                byReview => (IReadOnlyList<ReviewComment>)[.. byReview.Select(comment => new ReviewComment(Json.Text(comment, "path"), Json.Number(comment, "line"), Json.Text(comment, "body")))],
                StringComparer.Ordinal);
}
