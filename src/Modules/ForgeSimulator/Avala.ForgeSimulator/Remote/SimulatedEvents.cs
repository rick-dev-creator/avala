using System.Globalization;
using Avala.ForgeSimulator.Scenarios;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.ForgeSimulator.Remote;

internal sealed record SimulatedIssue(int Number, string Title, string Body, string Author)
{
    public bool PullRequest { get; init; }

    public bool Fork { get; init; }
}

internal sealed record SimulatedEvent(long Id, string Kind, int Number, string Actor)
{
    public string Label { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    public string Reviewer { get; init; } = string.Empty;
}

internal sealed record SimulatedFailure(string Head, string Author);

internal static class SimulatedEvents
{
    private static readonly Dictionary<ForgeEventKind, string> Kinds = new()
    {
        [ForgeEventKind.IssueOpened] = "opened",
        [ForgeEventKind.IssueLabeled] = "labeled",
        [ForgeEventKind.CommentCommand] = "commented",
        [ForgeEventKind.ReviewRequested] = "review-requested",
    };

    public static ForgeEventPage Since(SimulatedRemote remote, ForgeEventQuery query, Uri site)
    {
        var newest = remote.Events.Count == 0 ? 0 : remote.Events.Max(happened => happened.Id);

        if (query.Cursor.Bind(Number).IsNone)
        {
            return new ForgeEventPage([], Text(newest));
        }

        var cursor = query.Cursor.Bind(Number).Match(found => found, () => 0L);
        var fresh = remote.Events.Where(happened => happened.Id > cursor && happened.Kind == Kinds[query.Kind]).OrderBy(happened => happened.Id).ToList();
        var taken = fresh.Take(query.Most).ToList();
        var reached = fresh.Count > taken.Count ? taken[^1].Id : Math.Max(cursor, newest);

        return new ForgeEventPage([.. taken.Select(happened => Event(remote, query.Kind, happened, site))], Text(reached));
    }

    public static ForgeEventPage Checks(SimulatedRemote remote, ForgeEventQuery query, string branch, string head)
    {
        var failure = remote.FailingHeads.Find(failing => failing.Head == head);
        var state = $"{(failure is null ? "seen" : "failed")}:{head}";

        if (failure is null || query.Cursor.Match(cursor => cursor == state, () => true))
        {
            return new ForgeEventPage([], state);
        }

        return new ForgeEventPage(
            [
                new ForgeEvent(ForgeEventKind.ChecksFailed, $"{branch}:{head}", state, failure.Author)
                {
                    Author = failure.Author,
                    Branch = branch,
                    Head = head,
                    Title = $"Checks failed on {branch}",
                    Checks = [new CheckRun("build", CheckStatus.Failed, Option<Uri>.None, ForgeScenario.FailingTest)],
                },
            ],
            state);
    }

    private static ForgeEvent Event(SimulatedRemote remote, ForgeEventKind kind, SimulatedEvent happened, Uri site)
    {
        var issue = remote.Issues.Find(known => known.Number == happened.Number) ?? new SimulatedIssue(happened.Number, string.Empty, string.Empty, string.Empty);
        var number = happened.Number.ToString(CultureInfo.InvariantCulture);

        return new ForgeEvent(kind, Text(happened.Id), Text(happened.Id), happened.Actor)
        {
            Number = happened.Number,
            Title = issue.Title,
            Body = issue.Body,
            Author = issue.Author,
            Link = new Uri(site, $"{(issue.PullRequest ? "pulls" : "issues")}/{number}"),
            Label = Present(happened.Label),
            Comment = Present(happened.Comment),
            Reviewer = Present(happened.Reviewer),
            PullRequest = issue.PullRequest,
            FromFork = issue.Fork,
        };
    }

    private static Option<long> Number(string cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var found) ? found : Option<long>.None;

    private static Option<string> Present(string text) => text.Length > 0 ? text : Option<string>.None;

    private static string Text(long number) => number.ToString(CultureInfo.InvariantCulture);
}
