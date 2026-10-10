using System.Net;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.GitHub.Api;
using Avala.Sdk;
using Avala.Testing;

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
    [InlineData("https://api.github.com", "")]
    [InlineData("https://ghe.example.com/api/v3", "ghe.example.com")]
    public void TheGhCliCallsTheSameRequestWithItsFieldsAndTheHostOfAnEnterpriseServer(string api, string host)
    {
        var request = new ForgeRequest(ForgeMethod.Post, $"{Repository}/pulls") { Fields = [new("title", "Add a greeting")] };

        var call = Outcomes.Present(new GitHubForge().CliFor(request, Targets.Of(new Uri(api))));

        Assert.Equal(("gh", CliStatusChannel.Output), (call.Command, call.Status));
        Assert.Equal(
            ["api", "--method", "POST", "--include", "-H", "Accept: application/vnd.github+json", .. host.Length > 0 ? ["--hostname", host] : Array.Empty<string>(), "-f", "title=Add a greeting", "repos/octo/shop/pulls"],
            call.Arguments);
    }

    [Fact]
    public async Task GitHubPassesTheForgeConformanceKitAsync()
    {
        var server = await ServerAsync();

        Assert.Empty(await ForgeConformance.CheckAsync(new ConformanceCase(new GitHubForge(), new ForgeContext(server.Api(Root), Targets.Of(Root)), Head, "main"), Cancellation));
    }

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
