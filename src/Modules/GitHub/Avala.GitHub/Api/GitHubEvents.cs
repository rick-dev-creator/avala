using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.GitHub.Api;

internal sealed record Listed(long Id, JsonElement Item);

internal static class GitHubEvents
{
    public static async Task<Result<ForgeEventPage, ForgeError>> ReadAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken) =>
        query.Kind switch
        {
            ForgeEventKind.IssueOpened => await OpenedAsync(context, query, cancellationToken),
            ForgeEventKind.CommentCommand => await CommentedAsync(context, query, cancellationToken),
            ForgeEventKind.ChecksFailed => await ChecksAsync(context, query, cancellationToken),
            _ => await IssueEventsAsync(context, query, cancellationToken),
        };

    private static async Task<Result<ForgeEventPage, ForgeError>> OpenedAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        if (!(await GetAsync(context, $"{Repository(context)}/issues?state=all&sort=created&direction=desc&per_page=100", cancellationToken)).TryGetValue(out var body, out var error))
        {
            return error;
        }

        return Json.Read(body, root => Window(
            [.. root.EnumerateArray().Select(issue => new Listed(issue.GetProperty("number").GetInt64(), issue.Clone()))],
            listed => !listed.Item.TryGetProperty("pull_request", out _),
            query,
            listed => Issue(ForgeEventKind.IssueOpened, listed.Id, listed.Item, Json.Text(listed.Item.GetProperty("user"), "login"))));
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> IssueEventsAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        var wanted = query.Kind == ForgeEventKind.IssueLabeled ? "labeled" : "review_requested";

        if (!(await GetAsync(context, $"{Repository(context)}/issues/events?per_page=100", cancellationToken)).TryGetValue(out var body, out var error)
            || !Json.Read(body, root => Window(
                [.. root.EnumerateArray().Select(happened => new Listed(happened.GetProperty("id").GetInt64(), happened.Clone()))],
                listed => Json.Text(listed.Item, "event") == wanted && listed.Item.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.Object,
                query,
                listed => Issue(query.Kind, listed.Id, listed.Item.GetProperty("issue"), Login(listed.Item, "actor")) with
                {
                    Label = Named(listed.Item, "label", "name"),
                    Reviewer = Named(listed.Item, "requested_reviewer", "login"),
                })).TryGetValue(out var page, out error))
        {
            return error;
        }

        return await ForksAsync(context, page, cancellationToken);
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> CommentedAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        if (!(await GetAsync(context, $"{Repository(context)}/issues/comments?sort=created&direction=desc&per_page=100", cancellationToken)).TryGetValue(out var body, out var error)
            || !Json.Read(body, root => Window(
                [.. root.EnumerateArray().Select(comment => new Listed(comment.GetProperty("id").GetInt64(), comment.Clone()))],
                _ => true,
                query,
                listed => new ForgeEvent(ForgeEventKind.CommentCommand, Text(listed.Id), Text(listed.Id), Login(listed.Item, "user"))
                {
                    Number = IssueNumber(Json.Text(listed.Item, "issue_url")),
                    Comment = Json.Text(listed.Item, "body"),
                    Link = Json.Link(listed.Item, "html_url"),
                })).TryGetValue(out var page, out error))
        {
            return error;
        }

        var events = new List<ForgeEvent>();

        foreach (var comment in page.Events)
        {
            if (!(await GetAsync(context, $"{Repository(context)}/issues/{Text(comment.Number)}", cancellationToken)).Bind(issue => Json.Read(issue, root => comment with
                {
                    Title = Json.Text(root, "title"),
                    Body = Json.Text(root, "body"),
                    Author = Login(root, "user"),
                    PullRequest = root.TryGetProperty("pull_request", out _),
                })).TryGetValue(out var known, out error))
            {
                return error;
            }

            events.Add(known);
        }

        return await ForksAsync(context, page with { Events = events }, cancellationToken);
    }

    private static async Task<Result<ForgeEventPage, ForgeError>> ChecksAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken)
    {
        var branch = query.Branch.Match(name => name, () => string.Empty);

        if (!(await GetAsync(context, $"{Repository(context)}/commits/{Uri.EscapeDataString(branch)}", cancellationToken))
            .Bind(body => Json.Read(body, root => (Sha: Json.Text(root, "sha"), Author: Author(root), Link: Json.Link(root, "html_url")))).TryGetValue(out var head, out var error)
            || !(await GetAsync(context, $"{Repository(context)}/commits/{head.Sha}/check-runs", cancellationToken)).Bind(body => Json.Read(body, GitHubForge.CheckRuns)).TryGetValue(out var runs, out error)
            || !(await GetAsync(context, $"{Repository(context)}/commits/{head.Sha}/status", cancellationToken)).Bind(body => Json.Read(body, GitHubForge.Statuses)).TryGetValue(out var statuses, out error))
        {
            return error;
        }

        IReadOnlyList<CheckRun> checks = [.. runs, .. statuses];
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

            if (!(await GetAsync(context, $"{Repository(context)}/pulls/{Text(happened.Number)}", cancellationToken)).Bind(body => Json.Read(body, Forked)).TryGetValue(out var fork, out var error))
            {
                return error;
            }

            events.Add(happened with { FromFork = fork });
        }

        return page with { Events = events };
    }

    private static ForgeEventPage Window(IReadOnlyList<Listed> listed, Func<Listed, bool> relevant, ForgeEventQuery query, Func<Listed, ForgeEvent> read)
    {
        var newest = listed.Count == 0 ? 0 : listed.Max(item => item.Id);
        var cursor = query.Cursor.Bind(Number);

        if (cursor.IsNone)
        {
            return new ForgeEventPage([], Text(newest));
        }

        var since = cursor.Match(found => found, () => 0L);
        var fresh = listed.Where(item => item.Id > since && relevant(item)).OrderBy(item => item.Id).ToList();
        var taken = fresh.Take(query.Most).ToList();

        return new ForgeEventPage([.. taken.Select(read)], Text(fresh.Count > taken.Count ? taken[^1].Id : Math.Max(since, newest)));
    }

    private static ForgeEvent Issue(ForgeEventKind kind, long id, JsonElement issue, string actor) =>
        new(kind, Text(id), Text(id), actor)
        {
            Number = issue.GetProperty("number").GetInt32(),
            Title = Json.Text(issue, "title"),
            Body = Json.Text(issue, "body"),
            Link = Json.Link(issue, "html_url"),
            Author = Login(issue, "user"),
            PullRequest = issue.TryGetProperty("pull_request", out _),
        };

    private static bool Forked(JsonElement pull) =>
        !(pull.GetProperty("head").TryGetProperty("repo", out var head) && head.ValueKind == JsonValueKind.Object)
        || Json.Text(head, "full_name") != Json.Text(pull.GetProperty("base").GetProperty("repo"), "full_name");

    private static string Author(JsonElement commit) =>
        commit.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? Json.Text(author, "login") : string.Empty;

    private static string Login(JsonElement item, string field) =>
        item.TryGetProperty(field, out var user) && user.ValueKind == JsonValueKind.Object ? Json.Text(user, "login") : string.Empty;

    private static Option<string> Named(JsonElement item, string field, string name) =>
        item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Object && Json.Text(value, name) is { Length: > 0 } text ? text : Option<string>.None;

    private static int IssueNumber(string url) =>
        int.TryParse(url[(url.LastIndexOf('/') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static Option<long> Number(string cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var found) ? found : Option<long>.None;

    private static string Text(long number) => number.ToString(CultureInfo.InvariantCulture);

    private static async Task<Result<string, ForgeError>> GetAsync(ForgeContext context, string path, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, path), cancellationToken)).Map(response => response.Body);

    private static string Repository(ForgeContext context) =>
        $"/repos/{Uri.EscapeDataString(context.Target.Repository.Owner)}/{Uri.EscapeDataString(context.Target.Repository.Name)}";
}
