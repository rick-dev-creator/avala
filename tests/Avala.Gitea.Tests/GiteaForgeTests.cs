using System.Net;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Gitea.Api;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

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

    [Theory]
    [InlineData(null)]
    [InlineData("codeberg")]
    public void TheTeaCliCallsThePathBelowTheApiRootWithTypedFlagsOnTheLoginItNamesAndReadsTheStatusFromStandardError(string? login)
    {
        var request = new ForgeRequest(ForgeMethod.Post, $"{Repository}/issues/7/comments") { Fields = [new("body", "Held.")], Flags = [new("private", true)] };

        var call = Outcomes.Present(Forge("forgejo").CliFor(request, Targets.Of(Root) with { Login = login.ToOption() }));

        Assert.Equal(("tea", CliStatusChannel.Error), (call.Command, call.Status));
        Assert.Equal(["api", "--method", "POST", "--include", .. login is null ? Array.Empty<string>() : ["--login", login], "--field", "body=Held.", "-F", "private=true", "repos/octo/shop/issues/7/comments"], call.Arguments);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task GiteaAndForgejoPassTheForgeConformanceKitAsync(string dialect)
    {
        var server = await ServerAsync();

        Assert.Empty(await ForgeConformance.CheckAsync(new ConformanceCase(Forge(dialect), new ForgeContext(server.Api(Root), Targets.Of(Root)), Head, "main"), Cancellation));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task ANewIssueIsAnEventAfterTheCursorAndAPullRequestListedAmongTheIssuesIsNotAsync(string dialect)
    {
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var baseline = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.IssueOpened, Option<string>.None), Cancellation));
        var page = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.IssueOpened, "10"), Cancellation));

        Assert.Equal(([], "13"), (baseline.Events, baseline.Cursor));
        Assert.Equal([(11, "carol", "Checkout button does nothing"), (13, "mallory", "Search returns stale results")], page.Events.Select(opened => (opened.Number, opened.Actor, opened.Title)));
        Assert.Equal("13", page.Cursor);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task LabelsAddedAndReviewRequestsComeFromTheTimelinesOfTheIssuesUpdatedSinceTheCursorAsync(string dialect)
    {
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var baseline = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, Option<string>.None), Cancellation));
        var labels = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, Since), Cancellation));
        var reviews = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.ReviewRequested, Since), Cancellation));

        Assert.Equal(([], $"0@{Eight.ToUnixTimeSeconds()}"), (baseline.Events, baseline.Cursor));
        Assert.Equal(
            [("7001", 11, "bob", Option<string>.Some("bug"), "carol"), ("7003", 11, "alice", Option<string>.Some("avala"), "carol")],
            labels.Events.Select(labeled => (labeled.Id, labeled.Number, labeled.Actor, labeled.Label, labeled.Author)));
        Assert.Equal($"7004@{Eight.ToUnixTimeSeconds()}", labels.Cursor);
        var review = Assert.Single(reviews.Events);
        Assert.Equal((7, "alice", Option<string>.Some("avala-bot"), true, false), (review.Number, review.Actor, review.Reviewer, review.PullRequest, review.FromFork));
        Assert.Contains(server.Sent, sent => sent.PathAndQuery == $"{Repository}/issues/11/timeline?since=2026-10-11T08%3A00%3A00Z&limit=50");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task ACommentIsReadWithTheIssueItWasLeftOnAsync(string dialect)
    {
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var page = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.CommentCommand, $"9001@{Eight.ToUnixTimeSeconds()}"), Cancellation));

        var comment = Assert.Single(page.Events);
        Assert.Equal(
            ("9002", 11, "alice", Option<string>.Some("/avala fix the checkout\nIt broke yesterday."), "Checkout button does nothing", "carol"),
            (comment.Id, comment.Number, comment.Actor, comment.Comment, comment.Title, comment.Author));
        Assert.Equal($"9002@{Eight.ToUnixTimeSeconds()}", page.Cursor);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task FailedStatusesOnABranchHeadWithNoneStillPendingAreOneEventPerHeadAsync(string dialect)
    {
        var finished = (await FixtureServer.FixtureAsync("gitea", "status", Cancellation)).Replace("\"status\": \"pending\"", "\"status\": \"success\"", StringComparison.Ordinal);
        var server = await EventServerAsync();
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var pending = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, "seen:e5bd391"), Cancellation));
        server.Route("GET", $"{Repository}/commits/{Sha}/status", HttpStatusCode.OK, finished);
        var failed = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, "seen:e5bd391"), Cancellation));
        var again = Outcomes.Succeeds(await Forge(dialect).ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, failed.Cursor), Cancellation));

        Assert.Equal(([], $"seen:{Sha}"), (pending.Events, pending.Cursor));
        var failure = Assert.Single(failed.Events);
        Assert.Equal(($"main:{Sha}", "alice", Option<string>.Some(Sha), "ci / tests (push)"), (failure.Id, failure.Actor, failure.Head, failure.Checks.Single(check => check.Status == CheckStatus.Failed).Name));
        Assert.Empty(again.Events);
    }

    [Fact]
    public void GiteaSignsItsWebhooksWithXGiteaSignatureAndForgejoAlsoWithXForgejoSignature()
    {
        Assert.Equal(["X-Gitea-Signature"], Forge("gitea").Signature.Headers);
        Assert.Equal(string.Empty, Forge("gitea").Signature.Prefix);
        Assert.Equal(["X-Forgejo-Signature", "X-Gitea-Signature"], Forge("forgejo").Signature.Headers);
    }

    private static readonly DateTimeOffset Eight = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private static string Since => $"7000@{Eight.ToUnixTimeSeconds()}";

    private static ForgeEventQuery Query(ForgeEventKind kind, Option<string> cursor) =>
        new(kind, cursor, Eight.AddMinutes(10)) { Branch = "main" };

    private static async Task<FixtureServer> EventServerAsync() =>
        (await ServerAsync())
            .Route("GET", $"{Repository}/issues?state=all&type=issues&limit=50", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "issues", Cancellation))
            .Route("GET", $"{Repository}/issues?state=all&type=all&since=2026-10-11T08%3A00%3A00Z&limit=50", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "issues-updated", Cancellation))
            .Route("GET", $"{Repository}/issues/11/timeline?since=2026-10-11T08%3A00%3A00Z&limit=50", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "timeline-11", Cancellation))
            .Route("GET", $"{Repository}/issues/7/timeline?since=2026-10-11T08%3A00%3A00Z&limit=50", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "timeline-7", Cancellation))
            .Route("GET", $"{Repository}/issues/comments?since=2026-10-11T08%3A00%3A00Z&limit=50", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "issue-comments", Cancellation))
            .Route("GET", $"{Repository}/issues/11", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "issue", Cancellation))
            .Route("GET", $"{Repository}/branches/main", HttpStatusCode.OK, await FixtureServer.FixtureAsync("gitea", "branch", Cancellation));

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
