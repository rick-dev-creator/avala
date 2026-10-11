using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Gitea.Api;

internal sealed record Listed(long Id, JsonElement Item);

internal sealed record TimeCursor(long Last, DateTimeOffset Since)
{
    public static TimeSpan Margin { get; } = TimeSpan.FromMinutes(10);

    public string Text => $"{Last.ToString(CultureInfo.InvariantCulture)}@{Since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";

    public string Query => Uri.EscapeDataString(Since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    public static Option<TimeCursor> Parse(string text) =>
        text.Split('@') is [var last, var since]
        && long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && long.TryParse(since, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds)
            ? new TimeCursor(id, DateTimeOffset.FromUnixTimeSeconds(seconds))
            : Option<TimeCursor>.None;

    public TimeCursor After(IReadOnlyList<Listed> taken, bool truncated, long newest, DateTimeOffset now) =>
        truncated
            ? this with { Last = taken[^1].Id }
            : new TimeCursor(Math.Max(Last, newest), now - Margin > Since ? now - Margin : Since);
}

internal static class GiteaEvents
{
    private const int PageSize = 50;

    public static async Task<Result<ForgeEventPage, ForgeError>> ReadAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken) =>
        query.Kind switch
        {
            ForgeEventKind.IssueOpened => await OpenedAsync(context, query, cancellationToken),
            ForgeEventKind.CommentCommand => await CommentedAsync(context, query, cancellationToken),
            ForgeEventKind.ChecksFailed => await ChecksAsync(context, query, cancellationToken),
            _ => await TimelinesAsync(context, query, cancellationToken),
        };

    private static async Task<Result<ForgeEventPage, ForgeError>> OpenedAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        if (!(await GetAsync(context, $"{Repository(context)}/issues?state=all&type=issues&limit={PageSize}", cancellationToken)).TryGetValue(out var body, out var error))
        {
            return error;
        }

        return Json.Read(body, root =>
        {
            var issues = root.EnumerateArray().Select(issue => new Listed(issue.GetProperty("number").GetInt64(), issue.Clone())).ToList();
            var newest = issues.Count == 0 ? 0 : issues.Max(issue => issue.Id);

            if (query.Cursor.Bind(Number).IsNone)
            {
                return new ForgeEventPage([], Text(newest));
            }

            var since = query.Cursor.Bind(Number).Match(found => found, () => 0L);
            var fresh = issues.Where(issue => issue.Id > since && !PullRequest(issue.Item)).OrderBy(issue => issue.Id).ToList();
            var taken = fresh.Take(query.Most).ToList();

            return new ForgeEventPage(
                [.. taken.Select(issue => Issue(ForgeEventKind.IssueOpened, issue.Id, issue.Item, Login(issue.Item, "user")))],
                Text(fresh.Count > taken.Count ? taken[^1].Id : Math.Max(since, newest)));
        });
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> TimelinesAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        if (query.Cursor.Bind(TimeCursor.Parse) is not { IsSome: true } known)
        {
            return new ForgeEventPage([], new TimeCursor(0, query.Now - TimeCursor.Margin).Text);
        }

        var cursor = known.Match(found => found, () => new TimeCursor(0, query.Now));

        if (!(await GetAsync(context, $"{Repository(context)}/issues?state=all&type=all&since={cursor.Query}&limit={PageSize}", cancellationToken))
            .Bind(body => Json.Read(body, root => root.EnumerateArray().Select(issue => issue.Clone()).ToList())).TryGetValue(out var issues, out var error))
        {
            return error;
        }

        var seen = new List<Listed>();

        foreach (var issue in issues)
        {
            if (!(await GetAsync(context, $"{Repository(context)}/issues/{issue.GetProperty("number").GetInt64().ToString(CultureInfo.InvariantCulture)}/timeline?since={cursor.Query}&limit={PageSize}", cancellationToken))
                .Bind(body => Json.Read(body, root => root.EnumerateArray().Select(happened => new Listed(happened.GetProperty("id").GetInt64(), Paired(issue, happened))).ToList())).TryGetValue(out var timeline, out error))
            {
                return error;
            }

            seen.AddRange(timeline);
        }

        var fresh = seen.Where(happened => happened.Id > cursor.Last && Relevant(query.Kind, happened.Item.GetProperty("event"))).OrderBy(happened => happened.Id).ToList();
        var taken = fresh.Take(query.Most).ToList();
        var page = new ForgeEventPage(
            [.. taken.Select(happened => Timeline(query.Kind, happened))],
            cursor.After(taken, fresh.Count > taken.Count, seen.Count == 0 ? 0 : seen.Max(happened => happened.Id), query.Now).Text);

        return await ForksAsync(context, page, cancellationToken);
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> CommentedAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        if (query.Cursor.Bind(TimeCursor.Parse) is not { IsSome: true } known)
        {
            return new ForgeEventPage([], new TimeCursor(0, query.Now - TimeCursor.Margin).Text);
        }

        var cursor = known.Match(found => found, () => new TimeCursor(0, query.Now));

        if (!(await GetAsync(context, $"{Repository(context)}/issues/comments?since={cursor.Query}&limit={PageSize}", cancellationToken))
            .Bind(body => Json.Read(body, root => root.EnumerateArray().Select(comment => new Listed(comment.GetProperty("id").GetInt64(), comment.Clone())).ToList())).TryGetValue(out var comments, out var error))
        {
            return error;
        }

        var fresh = comments.Where(comment => comment.Id > cursor.Last).OrderBy(comment => comment.Id).ToList();
        var taken = fresh.Take(query.Most).ToList();
        var events = new List<ForgeEvent>();

        foreach (var comment in taken)
        {
            var number = IssueNumber(Json.Text(comment.Item, "issue_url"));

            if (!(await GetAsync(context, $"{Repository(context)}/issues/{number.ToString(CultureInfo.InvariantCulture)}", cancellationToken))
                .Bind(body => Json.Read(body, issue => Issue(ForgeEventKind.CommentCommand, comment.Id, issue, Login(comment.Item, "user")) with
                {
                    Comment = Json.Text(comment.Item, "body"),
                    Link = Json.Link(comment.Item, "html_url"),
                })).TryGetValue(out var happened, out error))
            {
                return error;
            }

            events.Add(happened);
        }

        var newest = comments.Count == 0 ? 0 : comments.Max(comment => comment.Id);

        return await ForksAsync(context, new ForgeEventPage(events, cursor.After(taken, fresh.Count > taken.Count, newest, query.Now).Text), cancellationToken);
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> ChecksAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        var branch = query.Branch.Match(name => name, () => string.Empty);

        if (!(await GetAsync(context, $"{Repository(context)}/branches/{Uri.EscapeDataString(branch)}", cancellationToken))
            .Bind(body => Json.Read(body, root => Head(root.GetProperty("commit")))).TryGetValue(out var head, out var error)
            || !(await GetAsync(context, $"{Repository(context)}/commits/{Uri.EscapeDataString(head.Sha)}/status", cancellationToken)).Bind(body => Json.Read(body, GiteaForge.Statuses)).TryGetValue(out var checks, out error))
        {
            return error;
        }

        var failing = checks.Any(check => check.Status == CheckStatus.Failed) && checks.All(check => check.Status != CheckStatus.Pending);
        var state = $"{(failing ? "failed" : "seen")}:{head.Sha}";

        return !failing || query.Cursor.Match(cursor => cursor == state, () => true)
            ? new ForgeEventPage([], state)
            : new ForgeEventPage(
                [
                    new ForgeEvent(ForgeEventKind.ChecksFailed, $"{branch}:{head.Sha}", state, head.Author)
                    {
                        Author = head.Author,
                        Title = $"Checks failed on {branch}",
                        Branch = branch,
                        Head = head.Sha,
                        Link = head.Link,
                        Checks = checks,
                    },
                ],
                state);
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> ForksAsync(ForgeContext context, ForgeEventPage page, CancellationToken cancellationToken)
    {
        var events = new List<ForgeEvent>();

        foreach (var happened in page.Events)
        {
            if (!happened.PullRequest)
            {
                events.Add(happened);
                continue;
            }

            if (!(await GetAsync(context, $"{Repository(context)}/pulls/{happened.Number.ToString(CultureInfo.InvariantCulture)}", cancellationToken)).Bind(body => Json.Read(body, Forked)).TryGetValue(out var fork, out var error))
            {
                return error;
            }

            events.Add(happened with { FromFork = fork });
        }

        return page with { Events = events };
    }

    private static JsonElement Paired(JsonElement issue, JsonElement happened)
    {
        using var paired = JsonDocument.Parse($$"""{ "issue": {{issue.GetRawText()}}, "event": {{happened.GetRawText()}} }""");

        return paired.RootElement.Clone();
    }

    private static bool Relevant(ForgeEventKind kind, JsonElement happened) =>
        kind == ForgeEventKind.IssueLabeled
            ? Json.Text(happened, "type") == "label" && Json.Text(happened, "body") == "1"
            : Json.Text(happened, "type") == "review_request" && Login(happened, "assignee").Length > 0;

    private static ForgeEvent Timeline(ForgeEventKind kind, Listed happened)
    {
        var item = happened.Item.GetProperty("event");

        return Issue(kind, happened.Id, happened.Item.GetProperty("issue"), Login(item, "user")) with
        {
            Label = item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.Object ? Json.Text(label, "name") : Option<string>.None,
            Reviewer = Login(item, "assignee") is { Length: > 0 } reviewer ? reviewer : Option<string>.None,
        };
    }

    private static ForgeEvent Issue(ForgeEventKind kind, long id, JsonElement issue, string actor) =>
        new(kind, Text(id), Text(id), actor)
        {
            Number = issue.GetProperty("number").GetInt32(),
            Title = Json.Text(issue, "title"),
            Body = Json.Text(issue, "body"),
            Link = Json.Link(issue, "html_url"),
            Author = Login(issue, "user"),
            PullRequest = PullRequest(issue),
        };

    private static (string Sha, string Author, Option<Uri> Link) Head(JsonElement commit) =>
        (Json.Text(commit, "id"), commit.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? Json.Text(author, "username") : string.Empty, Json.Link(commit, "url"));

    private static bool PullRequest(JsonElement issue) =>
        issue.TryGetProperty("pull_request", out var pull) && pull.ValueKind == JsonValueKind.Object;

    private static bool Forked(JsonElement pull) =>
        !(pull.GetProperty("head").TryGetProperty("repo", out var head) && head.ValueKind == JsonValueKind.Object)
        || Json.Text(head, "full_name") != Json.Text(pull.GetProperty("base").GetProperty("repo"), "full_name");

    private static string Login(JsonElement item, string field) =>
        item.TryGetProperty(field, out var user) && user.ValueKind == JsonValueKind.Object ? Json.Text(user, "login") : string.Empty;

    private static long IssueNumber(string url) =>
        long.TryParse(url[(url.LastIndexOf('/') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static Option<long> Number(string cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var found) ? found : Option<long>.None;

    private static string Text(long number) => number.ToString(CultureInfo.InvariantCulture);

    private static async Task<Result<string, ForgeError>> GetAsync(ForgeContext context, string path, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, path), cancellationToken)).Map(response => response.Body);

    private static string Repository(ForgeContext context) =>
        $"{GiteaForge.ApiRoot}/repos/{Uri.EscapeDataString(context.Target.Repository.Owner)}/{Uri.EscapeDataString(context.Target.Repository.Name)}";
}
