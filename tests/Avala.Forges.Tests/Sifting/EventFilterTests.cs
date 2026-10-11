using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Sifting;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Tests.Sifting;

public sealed class EventFilterTests
{
    private static readonly ForgeRule Labeled = new("github", ForgeEventKind.IssueLabeled, ["alice", "Bob"]) { Label = "avala" };

    private static readonly ForgeEvent LabeledByAlice = new(ForgeEventKind.IssueLabeled, "7001", "7001", "alice") { Number = 12, Label = "Avala", Author = "mallory" };

    [Fact]
    public void AnEventMatchingTheRuleByAListedActorFiresWhoeverOpenedTheIssue()
    {
        Assert.Equal(EventJudgement.Fires, EventFilter.Judge(Labeled, LabeledByAlice));
        Assert.Equal(EventJudgement.Fires, EventFilter.Judge(Labeled, LabeledByAlice with { Actor = "BOB" }));
    }

    [Theory]
    [InlineData("mallory")]
    [InlineData("")]
    public void AnEventWhoseActorIsNotOnTheAuthorsListIsNotAllowedEvenWhenTheAuthorIs(string actor) =>
        Assert.Equal(EventJudgement.NotAllowed, EventFilter.Judge(Labeled, LabeledByAlice with { Actor = actor, Author = "alice" }));

    [Fact]
    public void AnEventOnAPullRequestFromAForkIsFilteredWhoeverActs() =>
        Assert.Equal(EventJudgement.Filtered(FilterReason.FromFork), EventFilter.Judge(Labeled, LabeledByAlice with { PullRequest = true, FromFork = true }));

    [Fact]
    public void AnotherLabelCommandOrReviewerThanTheRulesIsFilteredBeforeTheActorIsChecked()
    {
        var command = new ForgeRule("github", ForgeEventKind.CommentCommand, ["alice"]) { Command = "/avala fix" };
        var review = new ForgeRule("github", ForgeEventKind.ReviewRequested, ["alice"]) { Reviewer = "avala-bot" };
        var comment = new ForgeEvent(ForgeEventKind.CommentCommand, "9", "9", "mallory") { Comment = "/avala fixed it" };

        Assert.Equal(EventJudgement.Filtered(FilterReason.OtherLabel), EventFilter.Judge(Labeled, LabeledByAlice with { Label = "bug", Actor = "mallory" }));
        Assert.Equal(EventJudgement.Filtered(FilterReason.OtherCommand), EventFilter.Judge(command, comment));
        Assert.Equal(EventJudgement.Filtered(FilterReason.OtherReviewer), EventFilter.Judge(review, new ForgeEvent(ForgeEventKind.ReviewRequested, "3", "3", "alice") { Reviewer = "carol" }));
        Assert.Equal(EventJudgement.Fires, EventFilter.Judge(review with { Reviewer = Option<string>.None }, new ForgeEvent(ForgeEventKind.ReviewRequested, "3", "3", "alice") { Reviewer = "carol" }));
    }

    [Theory]
    [InlineData("/avala fix", true)]
    [InlineData("  /Avala fix the checkout\nThanks!", true)]
    [InlineData("/avala fixes", false)]
    [InlineData("Please /avala fix", false)]
    public void ACommandIsTheFirstWordsOfTheCommentsFirstLine(string comment, bool commands) =>
        Assert.Equal(commands, EventFilter.Commands("/avala fix", comment));

    [Fact]
    public void ThePayloadCarriesTheNormalizedEventAndItsKeyNamesTheConnectionKindAndId()
    {
        var failing = new ForgeEvent(ForgeEventKind.ChecksFailed, "main:6dcb09b", "failed:6dcb09b", "alice")
        {
            Branch = "main",
            Head = "6dcb09b",
            Link = new Uri("https://github.com/octo/shop/commit/6dcb09b"),
            Checks = [new CheckRun("build", CheckStatus.Failed, Option<Uri>.None, "1 test failed")],
        };
        var rule = new ForgeRule("github", ForgeEventKind.ChecksFailed, ["alice"]) { Branch = "main" };

        using var payload = JsonDocument.Parse(EventPayload.Json(rule, failing));

        var root = payload.RootElement;
        Assert.Equal(
            ("checks-failed", "github", "main", "6dcb09b", "alice", "https://github.com/octo/shop/commit/6dcb09b", false),
            (root.GetProperty("event").GetString(), root.GetProperty("connection").GetString(), root.GetProperty("branch").GetString(), root.GetProperty("head").GetString(), root.GetProperty("actor").GetString(), root.GetProperty("url").GetString(), root.GetProperty("pullRequest").GetBoolean()));
        Assert.Equal("failed", root.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.Equal("1 test failed", root.GetProperty("checks")[0].GetProperty("summary").GetString());
        Assert.Equal("forge:github:checks-failed:main:6dcb09b", EventPayload.KeyOf(rule, failing));
    }
}
