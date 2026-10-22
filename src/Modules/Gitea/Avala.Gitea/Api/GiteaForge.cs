using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Gitea.Api;

internal sealed record GiteaDialect(string Id, string Name, string Cli, IReadOnlyList<string> SignatureHeaders, string Logo)
{
    public static Uri Codeberg { get; } = new("https://codeberg.org");

    public Option<Uri> Suggested { get; init; }

    public static GiteaDialect Gitea { get; } = new(
        "gitea",
        "Gitea",
        "tea",
        ["X-Gitea-Signature"],
        "F0 M1.5 4.5H11.5V9A4 4 0 0 1 7.5 13H5.5A4 4 0 0 1 1.5 9ZM11.5 5.5H12.75A2.25 2.25 0 0 1 12.75 10H11.3A5 5 0 0 0 11.45 8.6H12.75A0.85 0.85 0 0 0 12.75 6.9H11.5ZM5.2 6.4L7.6 6.4L7.6 8.2L9.4 8.2L9.4 10.6L5.2 10.6Z");

    public static GiteaDialect Forgejo { get; } = new(
        "forgejo",
        "Forgejo",
        "tea",
        ["X-Forgejo-Signature", "X-Gitea-Signature"],
        "M2.7 13.5A1.8 1.8 0 1 1 6.3 13.5A1.8 1.8 0 1 1 2.7 13.5ZM10.2 3.5A1.8 1.8 0 1 1 13.8 3.5A1.8 1.8 0 1 1 10.2 3.5ZM10.2 9.5A1.8 1.8 0 1 1 13.8 9.5A1.8 1.8 0 1 1 10.2 9.5ZM3.6 12V6.5A3.9 3.9 0 0 1 7.5 2.6H10.6V4.4H7.5A2.1 2.1 0 0 0 5.4 6.5V12ZM5.4 11A3 3 0 0 1 8.4 8.6H10.6V10.4H8.4A1.2 1.2 0 0 0 7.2 11.6Z")
    {
        Suggested = Codeberg,
    };
}

internal sealed class GiteaForge(GiteaDialect dialect) : IForge, IForgeEvents, IForgeRepositories, IForgeAccounts
{
    public const string ApiRoot = "/api/v1";

    public ForgeInfo Info { get; } = new(dialect.Id, dialect.Name) { TokenScheme = "token", Logo = dialect.Logo, SuggestedUrl = dialect.Suggested };

    public HookSignature Signature { get; } = new(dialect.SignatureHeaders, string.Empty);

    public async ValueTask<Result<ForgeEventPage, ForgeError>> ReadSinceAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken) =>
        await GiteaEvents.ReadAsync(context, query, cancellationToken);

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target)
    {
        List<string> arguments = ["api", "--method", request.Method == ForgeMethod.Post ? "POST" : "GET", "--include"];
        arguments.AddRange(target.Login.Match<IEnumerable<string>>(login => ["--login", login], () => []));

        foreach (var (name, value) in request.Fields)
        {
            arguments.AddRange(["--field", $"{name}={value}"]);
        }

        foreach (var (name, value) in request.Flags)
        {
            arguments.AddRange(["-F", $"{name}={(value ? "true" : "false")}"]);
        }

        arguments.Add(request.Path.StartsWith(ApiRoot, StringComparison.Ordinal) ? request.Path[ApiRoot.Length..].TrimStart('/') : request.Path.TrimStart('/'));

        return new CliCall(dialect.Cli, arguments, CliStatusChannel.Error);
    }

    public string GitHost(Uri api) => api.Host;

    public async ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) =>
        await GiteaRepositories.CreateAsync(context, draft, cancellationToken);

    public async ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        await GiteaRepositories.ListAsync(context, cancellationToken);

    public async ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) =>
        await GiteaRepositories.MeAsync(context, cancellationToken);

    public async ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
        (await GetAsync(context, $"{Repository(context)}/pulls?state=open", cancellationToken))
            .Bind(body => Json.Read(body, root => root.EnumerateArray()
                .Where(pull => Json.Text(pull.GetProperty("head"), "ref") == head && FromTarget(context, pull))
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

        if (!(await GetAsync(context, path, cancellationToken)).Bind(body => Json.Read(body, root => Pull(context, root, pullRequest))).TryGetValue(out var pull, out var error)
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

    private static PullRequestState Pull(ForgeContext context, JsonElement pull, PullRequestRef known)
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

        return new PullRequestState(Known(Reference(pull), known), lifecycle, Json.Text(pull.GetProperty("head"), "sha"), mergeability) { HeadRepository = HeadRepository(context, pull) };
    }

    private static bool FromTarget(ForgeContext context, JsonElement pull) =>
        HeadRepository(context, pull).Match(
            head => string.Equals(head.Owner, context.Target.Repository.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(head.Name, context.Target.Repository.Name, StringComparison.OrdinalIgnoreCase),
            () => false);

    private static Option<RepositoryAddress> HeadRepository(ForgeContext context, JsonElement pull) =>
        pull.GetProperty("head").TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
        && Json.Text(repo, "full_name").Split('/') is [{ Length: > 0 } owner, { Length: > 0 } name]
            ? new RepositoryAddress(context.Target.Repository.Host, owner, name)
            : Option<RepositoryAddress>.None;

    private static PullRequestRef Known(PullRequestRef read, PullRequestRef known) =>
        read with { Head = read.Head.Length > 0 ? read.Head : known.Head, Base = read.Base.Length > 0 ? read.Base : known.Base };

    public static IReadOnlyList<CheckRun> Statuses(JsonElement root) =>
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
