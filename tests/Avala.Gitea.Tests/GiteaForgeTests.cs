using System.Net;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Gitea.Api;
using Avala.Testing;

namespace Avala.Gitea.Tests;

public sealed class GiteaForgeTests
{
    private const string Head = "avala/job-0b5c";

    private const string Sha = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    private const string Repository = "/api/v1/repos/octo/shop";

    private static readonly Uri Root = new("https://git.example.com");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Dialects => ["gitea", "forgejo"];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task OpeningAPullRequestPostsUnderTheApiRootWithTheTokenSchemeAsync(string dialect)
    {
        var forge = Forge(dialect);
        var server = await ServerAsync();
        var context = new ForgeContext(server.Api(Root, forge.Info.TokenScheme), Targets.Of(Root));

        var opened = Outcomes.Succeeds(await forge.OpenAsync(context, new PullRequestDraft(Head, "main", "Add a greeting", "Body"), Cancellation));

        Assert.Equal(new PullRequestRef(7, new Uri("https://git.example.com/octo/shop/pulls/7"), Head, "main"), opened);
        Assert.Equal(("POST", $"{Repository}/pulls", "token secret-value"), (server.Sent[0].Method, server.Sent[0].PathAndQuery, server.Sent[0].Authorization));
        Assert.Equal((dialect, "token"), (forge.Info.Id, forge.Info.TokenScheme));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task ReadingAPullRequestMapsItsCommitStatusesReviewsAndConflictsAsync(string dialect)
    {
        var server = await ServerAsync();
        var context = new ForgeContext(server.Api(Root, "token"), Targets.Of(Root));

        var state = Outcomes.Succeeds(await Forge(dialect).ReadAsync(context, Targets.Seven("https://git.example.com/octo/shop/pulls/7"), Cancellation));

        Assert.Equal((PullRequestLifecycle.Open, Sha, Mergeability.Conflicting), (state.Lifecycle, state.HeadCommit, state.Mergeability));
        Assert.Equal(
            [("ci / tests (push)", CheckStatus.Failed), ("ci / build (push)", CheckStatus.Passed), ("ci / lint (push)", CheckStatus.Pending)],
            state.Checks.Select(check => (check.Name, check.Status)));
        Assert.Equal([("80", "hubot", ReviewVerdict.ChangesRequested), ("82", "lisa", ReviewVerdict.Approved)], state.Reviews.Select(review => (review.Id, review.Author, review.Verdict)));
        Assert.Equal([new ReviewComment("GREETING.md", 1, "Name the team here.")], state.Reviews[0].Comments);
    }

    [Theory]
    [InlineData("\"merged\": false", "\"merged\": true", "Merged", "Conflicting")]
    [InlineData("\"state\": \"open\"", "\"state\": \"closed\"", "Closed", "Conflicting")]
    [InlineData("\"mergeable\": false", "\"mergeable\": true", "Open", "Mergeable")]
    public async Task APullRequestsLifecycleAndMergeabilityComeFromItsDocumentedFieldsAsync(string documented, string replaced, string lifecycle, string mergeability)
    {
        var pull = (await FixtureServer.FixtureAsync("gitea", "pull", Cancellation)).Replace(documented, replaced, StringComparison.Ordinal);
        var server = (await ServerAsync()).Route("GET", $"{Repository}/pulls/7", HttpStatusCode.OK, pull);

        var state = Outcomes.Succeeds(await Forge("gitea").ReadAsync(new ForgeContext(server.Api(Root), Targets.Of(Root)), Targets.Seven("https://git.example.com/octo/shop/pulls/7"), Cancellation));

        Assert.Equal((Enum.Parse<PullRequestLifecycle>(lifecycle), Enum.Parse<Mergeability>(mergeability)), (state.Lifecycle, state.Mergeability));
    }

    [Theory]
    [InlineData("success", "Passed")]
    [InlineData("pending", "Pending")]
    [InlineData("skipped", "Skipped")]
    [InlineData("failure", "Failed")]
    [InlineData("error", "Failed")]
    [InlineData("warning", "Failed")]
    public void ACommitStatusMapsToTheAgnosticCheckStatus(string status, string expected) =>
        Assert.Equal(Enum.Parse<CheckStatus>(expected), GiteaForge.CommitStatus(status));

    [Fact]
    public void TheTeaCliCallsThePathBelowTheApiRootAndReadsTheStatusFromStandardError()
    {
        var request = new ForgeRequest(ForgeMethod.Post, $"{Repository}/issues/7/comments") { Fields = [new("body", "Held.")] };

        var call = Outcomes.Present(Forge("forgejo").CliFor(request, Targets.Of(Root)));

        Assert.Equal(("tea", CliStatusChannel.Error), (call.Command, call.Status));
        Assert.Equal(["api", "--method", "POST", "--include", "--field", "body=Held.", "repos/octo/shop/issues/7/comments"], call.Arguments);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task GiteaAndForgejoPassTheForgeConformanceKitAsync(string dialect)
    {
        var server = await ServerAsync();

        Assert.Empty(await ForgeConformance.CheckAsync(new ConformanceCase(Forge(dialect), new ForgeContext(server.Api(Root), Targets.Of(Root)), Head, "main"), Cancellation));
    }

    private static GiteaForge Forge(string dialect) => new(dialect == "gitea" ? GiteaDialect.Gitea : GiteaDialect.Forgejo);

    private static async Task<FixtureServer> ServerAsync()
    {
        var pull = await FixtureServer.FixtureAsync("gitea", "pull", Cancellation);

        return new FixtureServer()
            .Route("GET", $"{Repository}/pulls?state=open", HttpStatusCode.OK, "[]", $"[{pull}]")
            .Route("POST", $"{Repository}/pulls", HttpStatusCode.Created, pull)
            .Route("GET", $"{Repository}/pulls/7", HttpStatusCode.OK, pull)
            .Route("GET", $"{Repository}/commits/{Sha}/status", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "status", Cancellation))
            .Route("GET", $"{Repository}/pulls/7/reviews", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "reviews", Cancellation))
            .Route("GET", $"{Repository}/pulls/7/reviews/80/comments", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "review-comments", Cancellation))
            .Route("POST", $"{Repository}/issues/7/comments", HttpStatusCode.Created, await FixtureServer.FixtureAsync("gitea", "comment", Cancellation));
    }
}
