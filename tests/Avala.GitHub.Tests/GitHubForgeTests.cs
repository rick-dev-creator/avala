using System.Net;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.GitHub.Api;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.GitHub.Tests;

public sealed class GitHubForgeTests
{
    private const string Head = "avala/job-0b5c";

    private const string Sha = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    private const string Repository = "/repos/octo/shop";

    private static readonly Uri Root = new("https://api.github.com");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpeningAPullRequestPostsTheDocumentedFieldsWithTheTokenAndReadsItsNumberAndLinkAsync()
    {
        var server = await ServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var opened = Outcomes.Succeeds(await new GitHubForge().OpenAsync(context, new PullRequestDraft(Head, "main", "Add a greeting", "Body"), Cancellation));

        Assert.Equal(new PullRequestRef(7, new Uri("https://github.com/octo/shop/pull/7"), Head, "main"), opened);
        var sent = Assert.Single(server.Sent);
        Assert.Equal(("POST", $"{Repository}/pulls", "Bearer secret-value"), (sent.Method, sent.PathAndQuery, sent.Authorization));
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(
            ["title=Add a greeting", $"head={Head}", "base=main", "body=Body"],
            body.RootElement.EnumerateObject().Select(field => $"{field.Name}={field.Value.GetString()}"));
    }

    [Fact]
    public async Task ReadingAPullRequestMapsItsChecksStatusesReviewsAndConflictsAsync()
    {
        var server = await ServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var state = Outcomes.Succeeds(await new GitHubForge().ReadAsync(context, Targets.Seven("https://github.com/octo/shop/pull/7"), Cancellation));

        Assert.Equal((PullRequestLifecycle.Open, Sha, Mergeability.Conflicting), (state.Lifecycle, state.HeadCommit, state.Mergeability));
        Assert.Equal(
            [("tests", CheckStatus.Failed, "1 test failed"), ("build", CheckStatus.Passed, string.Empty), ("lint", CheckStatus.Pending, string.Empty), ("coverage", CheckStatus.Failed, "Coverage dropped")],
            state.Checks.Select(check => (check.Name, check.Status, check.Summary)));
        Assert.Equal(new Uri("https://github.com/octo/shop/runs/4"), Outcomes.Present(state.Checks[0].Link));
        Assert.Equal([("80", "hubot", ReviewVerdict.ChangesRequested), ("82", "lisa", ReviewVerdict.Approved)], state.Reviews.Select(review => (review.Id, review.Author, review.Verdict)));
        Assert.Equal([new ReviewComment("GREETING.md", 1, "Name the team here.")], state.Reviews[0].Comments);
    }

    [Theory]
    [InlineData("\"merged\": false", "\"merged\": true", "Merged", "Conflicting")]
    [InlineData("\"state\": \"open\"", "\"state\": \"closed\"", "Closed", "Conflicting")]
    [InlineData("\"mergeable\": false,\n  \"rebaseable\": false,\n  \"mergeable_state\": \"dirty\"", "\"mergeable\": null,\n  \"rebaseable\": null,\n  \"mergeable_state\": \"unknown\"", "Open", "Unknown")]
    [InlineData("\"mergeable\": false,\n  \"rebaseable\": false,\n  \"mergeable_state\": \"dirty\"", "\"mergeable\": true,\n  \"rebaseable\": true,\n  \"mergeable_state\": \"clean\"", "Open", "Mergeable")]
    public async Task APullRequestsLifecycleAndMergeabilityComeFromItsDocumentedFieldsAsync(string documented, string replaced, string lifecycle, string mergeability)
    {
        var pull = (await FixtureServer.FixtureAsync("github", "pull", Cancellation)).Replace(documented, replaced, StringComparison.Ordinal);
        var server = (await ServerAsync()).Route("GET", $"{Repository}/pulls/7", HttpStatusCode.OK, pull);
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var state = Outcomes.Succeeds(await new GitHubForge().ReadAsync(context, Targets.Seven("https://github.com/octo/shop/pull/7"), Cancellation));

        Assert.Equal((Enum.Parse<PullRequestLifecycle>(lifecycle), Enum.Parse<Mergeability>(mergeability)), (state.Lifecycle, state.Mergeability));
    }

    [Fact]
    public async Task CommentingPostsTheBodyToThePullRequestsIssueCommentsAsync()
    {
        var server = await ServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var comment = Outcomes.Succeeds(await new GitHubForge().CommentAsync(context, Targets.Seven("https://github.com/octo/shop/pull/7"), "Held.", Cancellation));

        Assert.Equal(new CommentRef("1", new Uri("https://github.com/octo/shop/pull/7#issuecomment-1")), comment);
        Assert.Equal(("POST", $"{Repository}/issues/7/comments", """{"body":"Held."}"""), (server.Sent[^1].Method, server.Sent[^1].PathAndQuery, server.Sent[^1].Body));
    }

    [Theory]
    [InlineData("https://api.github.com", null, "")]
    [InlineData("https://ghe.example.com/api/v3", null, "ghe.example.com")]
    [InlineData("https://ghe.example.com/api/v3", "ghe.internal", "ghe.internal")]
    public void TheGhCliCallsTheSameRequestWithItsFieldsTypedFlagsAndTheHostOfAnEnterpriseServerOrTheLoginItNames(string api, string? login, string host)
    {
        var request = new ForgeRequest(ForgeMethod.Post, $"{Repository}/pulls") { Fields = [new("title", "Add a greeting")], Flags = [new("draft", false)] };

        var call = Outcomes.Present(new GitHubForge().CliFor(request, Targets.Of(new Uri(api)) with { Login = login.ToOption() }));

        Assert.Equal(("gh", CliStatusChannel.Output), (call.Command, call.Status));
        Assert.Equal(
            ["api", "--method", "POST", "--include", "-H", "Accept: application/vnd.github+json", .. host.Length > 0 ? ["--hostname", host] : Array.Empty<string>(), "-f", "title=Add a greeting", "-F", "draft=false", "repos/octo/shop/pulls"],
            call.Arguments);
    }

    [Fact]
    public async Task ANewIssueIsAnEventAfterTheCursorAndThePullRequestsListedAmongTheIssuesAreNotAsync()
    {
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var baseline = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.IssueOpened, Option<string>.None), Cancellation));
        var page = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.IssueOpened, "10"), Cancellation));

        Assert.Equal(([], "13"), (baseline.Events, baseline.Cursor));
        Assert.Equal([(11, "carol", "Checkout button does nothing", "Clicking it shows nothing."), (13, "mallory", "Search returns stale results", string.Empty)], page.Events.Select(opened => (opened.Number, opened.Actor, opened.Title, opened.Body)));
        Assert.Equal(("11", Option<Uri>.Some(new Uri("https://github.com/octo/shop/issues/11"))), (page.Events[0].Cursor, page.Events[0].Link));
        Assert.Equal("13", page.Cursor);
        Assert.Equal($"{Repository}/issues?state=all&sort=created&direction=desc&per_page=100", server.Sent[^1].PathAndQuery);
    }

    [Fact]
    public async Task LabelsAndReviewRequestsComeFromTheRepositorysIssueEventsWithTheirActorAndWhetherThePullRequestIsAForksAsync()
    {
        var forked = (await FixtureServer.FixtureAsync("github", "pull", Cancellation)).Replace("\"head\": {\n    \"label\": \"octo:avala/job-0b5c\",\n    \"ref\": \"avala/job-0b5c\",\n    \"sha\": \"6dcb09b5b57875f334f61aebed695e2e4193db5e\",\n    \"user\": { \"login\": \"octo\", \"id\": 2 },\n    \"repo\": { \"id\": 1296269, \"name\": \"shop\", \"full_name\": \"octo/shop\" }", "\"head\": {\n    \"ref\": \"patch-1\",\n    \"sha\": \"6dcb09b5b57875f334f61aebed695e2e4193db5e\",\n    \"repo\": { \"id\": 9, \"name\": \"shop\", \"full_name\": \"mallory/shop\" }", StringComparison.Ordinal);
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var labels = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, "7000"), Cancellation));
        var reviews = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.ReviewRequested, "7000"), Cancellation));
        server.Route("GET", $"{Repository}/pulls/7", HttpStatusCode.OK, forked);
        var fromFork = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.ReviewRequested, "7000"), Cancellation));

        Assert.Equal(
            [("7001", 11, "bob", Option<string>.Some("bug"), "carol"), ("7003", 11, "alice", Option<string>.Some("avala"), "carol")],
            labels.Events.Select(labeled => (labeled.Id, labeled.Number, labeled.Actor, labeled.Label, labeled.Author)));
        Assert.Equal("7003", labels.Cursor);
        var review = Assert.Single(reviews.Events);
        Assert.Equal((7, "alice", Option<string>.Some("avala-bot"), true, false), (review.Number, review.Actor, review.Reviewer, review.PullRequest, review.FromFork));
        Assert.True(Assert.Single(fromFork.Events).FromFork);
    }

    [Fact]
    public async Task ACommentIsReadWithTheIssueItWasLeftOnAsync()
    {
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var page = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.CommentCommand, "9001"), Cancellation));

        var comment = Assert.Single(page.Events);
        Assert.Equal(
            ("9002", 11, "alice", Option<string>.Some("/avala fix the checkout\nIt broke yesterday."), "Checkout button does nothing", "carol", false),
            (comment.Id, comment.Number, comment.Actor, comment.Comment, comment.Title, comment.Author, comment.PullRequest));
        Assert.Equal("9002", page.Cursor);
    }

    [Fact]
    public async Task FailedChecksOnABranchHeadWithNoneStillRunningAreOneEventPerHeadAsync()
    {
        var finished = (await FixtureServer.FixtureAsync("github", "check-runs", Cancellation)).Replace("\"status\": \"in_progress\",\n      \"conclusion\": null", "\"status\": \"completed\",\n      \"conclusion\": \"success\"", StringComparison.Ordinal);
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var running = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, "seen:e5bd391"), Cancellation));
        server.Route("GET", $"{Repository}/commits/{Sha}/check-runs", HttpStatusCode.OK, finished);
        var failed = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, "seen:e5bd391"), Cancellation));
        var again = Outcomes.Succeeds(await new GitHubForge().ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, failed.Cursor), Cancellation));

        Assert.Equal(([], $"seen:{Sha}"), (running.Events, running.Cursor));
        var failure = Assert.Single(failed.Events);
        Assert.Equal(($"main:{Sha}", "alice", Option<string>.Some("main"), Option<string>.Some(Sha), $"failed:{Sha}"), (failure.Id, failure.Actor, failure.Branch, failure.Head, failed.Cursor));
        Assert.Equal(["tests", "coverage"], failure.Checks.Where(check => check.Status == CheckStatus.Failed).Select(check => check.Name));
        Assert.Empty(again.Events);
    }

    [Fact]
    public void GitHubSignsItsWebhooksWithAPrefixedXHubSignature256()
    {
        Assert.Equal(["X-Hub-Signature-256"], new GitHubForge().Signature.Headers);
        Assert.Equal("sha256=", new GitHubForge().Signature.Prefix);
    }

    [Fact]
    public async Task TheOpenPullRequestOfABranchIsFoundAfterItsRepositoryWasRenamedAsync()
    {
        var pull = (await FixtureServer.FixtureAsync("github", "pull", Cancellation))
            .Replace("\"full_name\": \"octo/shop\"", "\"full_name\": \"octo/store\"", StringComparison.Ordinal)
            .Replace("\"name\": \"shop\"", "\"name\": \"store\"", StringComparison.Ordinal);
        var server = (await ServerAsync()).Route("GET", $"{Repository}/pulls?state=open&head=octo:{Uri.EscapeDataString(Head)}", HttpStatusCode.OK, $"[{pull}]");

        var found = Outcomes.Succeeds(await new GitHubForge().FindAsync(new ForgeContext(server.Api(Root), Targets.Of(Root)), Head, Cancellation));

        Assert.Equal(Option<int>.Some(7), found.Map(pullRequest => pullRequest.Number));
    }

    [Fact]
    public async Task GitHubPassesTheForgeConformanceKitAsync()
    {
        var server = await ServerAsync();
        const string Own = "\"full_name\": \"octo/shop\"";
        var pull = await FixtureServer.FixtureAsync("github", "pull", Cancellation);
        var head = pull.IndexOf(Own, StringComparison.Ordinal);
        var fork = $"{pull[..head]}\"full_name\": \"mallory/shop\"{pull[(head + Own.Length)..]}".Replace("\"number\": 7", "\"number\": 8", StringComparison.Ordinal);

        Assert.Empty(await ForgeConformance.CheckAsync(
            new ConformanceCase(new GitHubForge(), new ForgeContext(server.Api(Root), Targets.Of(Root)), Head, "main", _ => Task.FromResult(server.Route("GET", $"{Repository}/pulls?state=open&head=octo:{Uri.EscapeDataString(Head)}", HttpStatusCode.OK, $"[{fork}]", $"[{fork}, {pull}]"))),
            Cancellation));
    }

    private static ForgeEventQuery Query(ForgeEventKind kind, Option<string> cursor) =>
        new(kind, cursor, new DateTimeOffset(2026, 10, 11, 8, 10, 0, TimeSpan.Zero)) { Branch = "main" };

    private static async Task<FixtureServer> EventServerAsync() =>
        (await ServerAsync())
            .Route("GET", $"{Repository}/issues?state=all&sort=created&direction=desc&per_page=100", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "issues", Cancellation))
            .Route("GET", $"{Repository}/issues/events?per_page=100", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "issue-events", Cancellation))
            .Route("GET", $"{Repository}/issues/comments?sort=created&direction=desc&per_page=100", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "issue-comments", Cancellation))
            .Route("GET", $"{Repository}/issues/11", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "issue", Cancellation))
            .Route("GET", $"{Repository}/commits/main", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "commit", Cancellation));

    internal static async Task<FixtureServer> ServerAsync()
    {
        var pull = await FixtureServer.FixtureAsync("github", "pull", Cancellation);

        return new FixtureServer()
            .Route("GET", $"{Repository}/pulls?state=open&head=octo:{Uri.EscapeDataString(Head)}", HttpStatusCode.OK, "[]", $"[{pull}]")
            .Route("POST", $"{Repository}/pulls", HttpStatusCode.Created, pull)
            .Route("GET", $"{Repository}/pulls/7", HttpStatusCode.OK, pull)
            .Route("GET", $"{Repository}/commits/{Sha}/check-runs", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "check-runs", Cancellation))
            .Route("GET", $"{Repository}/commits/{Sha}/status", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "status", Cancellation))
            .Route("GET", $"{Repository}/pulls/7/reviews", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "reviews", Cancellation))
            .Route("GET", $"{Repository}/pulls/7/comments", HttpStatusCode.OK, await FixtureServer.FixtureAsync("github", "review-comments", Cancellation))
            .Route("POST", $"{Repository}/issues/7/comments", HttpStatusCode.Created, await FixtureServer.FixtureAsync("github", "comment", Cancellation));
    }
}
