using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Tests.Review;

public sealed class ReviewPhrasesTests
{
    [Fact]
    public void AFailedAttemptNamesEachFailingCheckAndShowsTheTailsOfTheirOutput()
    {
        var failed = new FailedAttempt(2,
        [
            Check("calculator", CheckStatus.Failed, 1, "add(2, 2) = 5", "assertion failed"),
            Check("e2e", CheckStatus.TimedOut, Option<int>.None, string.Empty, string.Empty),
            Check("lint", CheckStatus.NotFound, Option<int>.None, string.Empty, "lint: not found"),
        ]);

        Assert.Equal(
            ("Attempt 2 failed calculator (exit 1), e2e (timed out), lint (not found)", "add(2, 2) = 5\nassertion failed\nlint: not found"),
            ReviewPhrases.Exception(failed));
    }

    [Fact]
    public void AFailedCheckShowsOnlyTheLastTwelveLinesOfItsOutput()
    {
        var output = string.Join("\n", Enumerable.Range(1, 20).Select(line => $"line {line}"));

        var (_, detail) = ReviewPhrases.Exception(new FailedAttempt(1, [Check("tests", CheckStatus.Failed, 1, output, string.Empty)]));

        Assert.Equal(string.Join("\n", Enumerable.Range(9, 12).Select(line => $"line {line}")), detail);
    }

    [Theory]
    [InlineData("Command", "git push", "rule", "Denied: run git push", "by the rule no-push")]
    [InlineData("FileEdit", ".env", "", "Denied: edit .env", "by the default policy")]
    [InlineData("Web", "example.com", "", "Denied: reach example.com", "by the default policy")]
    [InlineData("Mcp", "deploy", "", "Denied: use deploy", "by the default policy")]
    public void APolicyDenialNamesTheRequestAndTheRuleThatDeniedIt(string kind, string target, string rule, string title, string detail)
    {
        var decision = new PolicyDecision(
            SessionId.New(),
            TurnId.New(),
            new ItemId("item"),
            JobId.New(),
            Enum.Parse<ItemKind>(kind),
            target,
            PolicyAnswer.Deny,
            rule.Length == 0 ? Option<PolicyRule>.None : new PolicyRule(RuleOrigin.Repository, "no-push", ItemKind.Command, "git push", RuleScope.Anywhere, PolicyAnswer.Deny),
            DecisionDelivery.Answered,
            DateTimeOffset.UnixEpoch);

        Assert.Equal((title, detail), ReviewPhrases.Exception(new PolicyDenial(decision)));
    }

    [Theory]
    [InlineData("Not on main", "Not on main")]
    [InlineData("", "")]
    public void AHumanDenialShowsTheMessageGivenWithIt(string message, string detail)
    {
        var answer = new HumanAnswer(
            SessionId.New(),
            JobId.New(),
            new ItemId("push"),
            ItemKind.Command,
            "git push",
            PermissionAnswer.Deny,
            message.Length == 0 ? Option<string>.None : message,
            Option<PolicyRule>.None,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(("You denied: run git push", detail), ReviewPhrases.Exception(new HumanDenial(answer)));
    }

    [Fact]
    public void ADeclinedFormNamesTheForm() =>
        Assert.Equal(("Declined: Database", string.Empty), ReviewPhrases.Exception(new DeclinedForm(Form([]))));

    [Theory]
    [InlineData("RecommendedOption", "PostgreSQL", "Assumed PostgreSQL for \"Which database?\"", "The policy took the recommended option.")]
    [InlineData("FirstOption", "SQLite", "Assumed SQLite for \"Which database?\"", "The policy took the first option.")]
    [InlineData("AgentJudgment", "", "Assumed the agent's judgment for \"Which database?\"", "The agent was told to decide.")]
    [InlineData("Confirmed", "PostgreSQL", "Assumed PostgreSQL for \"Which database?\"", "The policy confirmed it.")]
    public void AnAssumptionNamesWhatWasChosenAndWhy(string basis, string chosen, string title, string detail)
    {
        var assumption = new Assumption("database", "Which database?", Enum.Parse<AssumptionBasis>(basis), chosen.Length == 0 ? [] : [chosen]);

        Assert.Equal((title, detail), ReviewPhrases.Exception(new MadeAssumption(assumption)));
    }

    [Theory]
    [InlineData("Use the cache", "Use the cache")]
    [InlineData("", "")]
    public void AContinuationAfterAHoldShowsTheGuidanceGiven(string guidance, string detail)
    {
        var attempt = new AttemptRecord(3, AttemptOrigin.Hint, AttemptOutcome.Passed, guidance.Length == 0 ? Option<string>.None : guidance, Option<SessionId>.None);

        Assert.Equal(("Held, then continued on attempt 3", detail), ReviewPhrases.Exception(new ContinuedAfterHold(attempt)));
    }

    [Fact]
    public void AnEditedRuleFileAndUnreadableChangesExplainWhatTheyMeanForTheRules()
    {
        Assert.Equal(
            ("Edited a rule file: .avala/checks.json", "Its rules apply from the base commit, not from this edit."),
            ReviewPhrases.Exception(new EditedRuleFile(".avala/checks.json")));
        Assert.Equal(
            ("The diff could not be read", "Nothing proves the rule files are untouched."),
            ReviewPhrases.Exception(new UnreadableChanges()));
    }

    [Theory]
    [InlineData(JobRejection.MergeConflict, "The work conflicts with the base branch.")]
    [InlineData(JobRejection.BaseCheckoutDirty, "The base branch's checkout has uncommitted changes. Commit or stash them, then approve again.")]
    [InlineData(JobRejection.BaseMoved, "The base branch moved while merging. Approve again.")]
    [InlineData(JobRejection.NoBaseBranch, "The job's base is not a branch, so there is nothing to merge into.")]
    [InlineData(JobRejection.InvalidJobFile, "The repository's .avala/jobs.json names no usable approval strategy.")]
    [InlineData(JobRejection.UnknownApprovalStrategy, "The repository's .avala/jobs.json names no usable approval strategy.")]
    [InlineData(JobRejection.DeliveryFailed, "The delivery failed. The job still awaits review.")]
    [InlineData(JobRejection.ParentNotRunning, "The parent job no longer runs, so its child cannot be integrated.")]
    [InlineData(JobRejection.NotAwaitingReview, "The job no longer awaits review.")]
    public void ARefusedApprovalSaysWhatToDoNext(JobRejection rejection, string phrase) =>
        Assert.Equal(phrase, ReviewPhrases.Refusal(rejection));

    private static CheckEvidence Check(string name, CheckStatus status, Option<int> exitCode, string output, string error) =>
        new(name, name, status, exitCode, TimeSpan.FromSeconds(1), output, error);

    private static FormDecision Form(IReadOnlyList<Assumption> assumptions) =>
        new(
            SessionId.New(),
            TurnId.New(),
            new ItemId("question"),
            JobId.New(),
            new AgentForm(FormPurpose.Question, "Database", "Context", []),
            Autonomy.Autonomous,
            Option<FormAnswer>.None,
            assumptions,
            DecisionDelivery.Answered,
            DateTimeOffset.UnixEpoch);
}
