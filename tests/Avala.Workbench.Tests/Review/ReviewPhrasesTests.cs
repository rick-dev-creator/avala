using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;

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
            new ExceptionPhrase("Attempt 2 failed", "calculator · exit 1, e2e · timed out, lint · not found", string.Empty, "add(2, 2) = 5\nassertion failed\nlint: not found"),
            ReviewPhrases.Exception(failed));
        Assert.Equal(ExceptionTone.Failure, ReviewPhrases.Tone(failed));
    }

    [Fact]
    public void AFailedCheckShowsOnlyTheLastTwelveLinesOfItsOutput()
    {
        var output = string.Join("\n", Enumerable.Range(1, 20).Select(line => $"line {line}"));

        var tail = ReviewPhrases.Exception(new FailedAttempt(1, [Check("tests", CheckStatus.Failed, 1, output, string.Empty)])).Output;

        Assert.Equal(string.Join("\n", Enumerable.Range(9, 12).Select(line => $"line {line}")), tail);
    }

    [Theory]
    [InlineData("Command", "git push", "rule", "Denied: run git push", "rule no-push", "Denied by the rule no-push.")]
    [InlineData("FileEdit", ".env", "", "Denied: edit .env", "default policy", "Denied by the default policy.")]
    [InlineData("Web", "example.com", "", "Denied: reach example.com", "default policy", "Denied by the default policy.")]
    [InlineData("Mcp", "deploy", "", "Denied: use deploy", "default policy", "Denied by the default policy.")]
    public void APolicyDenialNamesTheRequestTheRuleThatDeniedItAndTheWholeTarget(string kind, string target, string rule, string title, string fact, string detail)
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

        Assert.Equal(new ExceptionPhrase(title, fact, detail, target), ReviewPhrases.Exception(new PolicyDenial(decision)));
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

        Assert.Equal(new ExceptionPhrase("You denied: run git push", "by you", detail, "git push"), ReviewPhrases.Exception(new HumanDenial(answer)));
    }

    [Fact]
    public void ADeclinedFormNamesTheForm() =>
        Assert.Equal(new ExceptionPhrase("Declined: Database", "form", string.Empty, string.Empty), ReviewPhrases.Exception(new DeclinedForm(Form([]))));

    [Theory]
    [InlineData("RecommendedOption", "PostgreSQL", "Assumed PostgreSQL for \"Which database?\"", "recommended option", "The policy took the recommended option.")]
    [InlineData("FirstOption", "SQLite", "Assumed SQLite for \"Which database?\"", "first option", "The policy took the first option.")]
    [InlineData("AgentJudgment", "", "Assumed the agent's judgment for \"Which database?\"", "agent's judgment", "The agent was told to decide.")]
    [InlineData("Confirmed", "PostgreSQL", "Assumed PostgreSQL for \"Which database?\"", "confirmed", "The policy confirmed it.")]
    public void AnAssumptionNamesWhatWasChosenAndWhy(string basis, string chosen, string title, string fact, string detail)
    {
        var assumption = new Assumption("database", "Which database?", Enum.Parse<AssumptionBasis>(basis), chosen.Length == 0 ? [] : [chosen]);

        Assert.Equal(new ExceptionPhrase(title, fact, detail, string.Empty), ReviewPhrases.Exception(new MadeAssumption(assumption)));
    }

    [Theory]
    [InlineData("Use the cache", "Use the cache")]
    [InlineData("", "")]
    public void AContinuationAfterAHoldShowsTheGuidanceGiven(string guidance, string detail)
    {
        var attempt = new AttemptRecord(3, AttemptOrigin.Hint, AttemptOutcome.Passed, guidance.Length == 0 ? Option<string>.None : guidance, Option<SessionId>.None);

        Assert.Equal(new ExceptionPhrase("Held, then continued on attempt 3", "held", detail, string.Empty), ReviewPhrases.Exception(new ContinuedAfterHold(attempt)));
    }

    [Fact]
    public void AnEditedRuleFileAndUnreadableChangesExplainWhatTheyMeanForTheRules()
    {
        Assert.Equal(
            new ExceptionPhrase("The agent edited a rule file", ".avala/checks.json", "Its rules apply from the base commit, not from this edit.", string.Empty),
            ReviewPhrases.Exception(new EditedRuleFile(".avala/checks.json")));
        Assert.Equal(
            new ExceptionPhrase("The diff could not be read", "rule files unproven", "Nothing proves the rule files are untouched.", string.Empty),
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

    [Theory]
    [InlineData(3, 0, "+3")]
    [InlineData(0, 2, "−2")]
    [InlineData(4, 1, "+4 −1")]
    public void AFileCountsOnlyTheSidesThatChanged(int added, int removed, string counts) =>
        Assert.Equal(counts, ReviewPhrases.Counts(new FileChange("routes.go", ChangeKind.Modified, added, removed)));

    [Fact]
    public void TheTotalsAddUpEveryFileAndABinaryFileCountsNothing()
    {
        var diff = new WorkspaceDiff(new WorkspaceId(Guid.NewGuid()), "ba5eba5e", "c0ffee", [
            new FileChange("ratelimit.go", ChangeKind.Added, 71, 0),
            new FileChange("routes.go", ChangeKind.Modified, 4, 1),
            new FileChange("logo.png", ChangeKind.Added, Option<int>.None, Option<int>.None),
        ]);

        Assert.Equal("+75 −1", ReviewPhrases.Totals(Result<WorkspaceDiff, WorkspaceFailure>.Success(diff)));
        Assert.Empty(ReviewPhrases.Totals(Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));
    }

    [Theory]
    [InlineData("Passed", "lint,vet,test", "lint, vet and test all passed in the worktree after the last turn.")]
    [InlineData("Passed", "test", "test passed in the worktree after the last turn.")]
    [InlineData("Failed", "lint,test", "test failed on the last attempt.")]
    [InlineData("NoChecksDeclared", "", "The repository declares no checks, so nothing ran.")]
    [InlineData("", "", "No checks ran in the worktree.")]
    public void TheProofUnderTheVerdictNamesTheChecksOfTheLastAttempt(string outcome, string checks, string proof)
    {
        var job = JobId.New();
        CheckEvidence[] ran = [.. checks.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(name =>
            Check(name, outcome == "Failed" && name == "test" ? CheckStatus.Failed : CheckStatus.Passed, name == "test" && outcome == "Failed" ? 1 : 0, string.Empty, string.Empty))];
        var evidence = new RunEvidence(job, new EvidenceSummary(1, Option<VerificationOutcome>.None, [], 0, 0, []), [])
        {
            Verifications = outcome.Length == 0 ? [] : [new VerificationReport(job, 1, Enum.Parse<VerificationOutcome>(outcome), Option<FileOrigin>.None, ran, GateVerdict.Pass, DateTimeOffset.UnixEpoch)],
        };

        Assert.Equal(proof, ReviewPhrases.Proof(evidence));
    }

    [Fact]
    public void TheFactsUnderTheTitleNameTheRepositoryConnectionAutonomyAndSessions()
    {
        var summary = new JobSummary(JobId.New(), "/home/dev/code/ledger-api/", "Rate-limit POST /login", DateTimeOffset.UnixEpoch, JobStatus.AwaitingReview, new ConnectionName("claude-work"), Autonomy.Autonomous, Option<WorkspaceId>.None);
        var history = new JobHistory(summary, [new SessionRecord(SessionId.New(), [1]), new SessionRecord(SessionId.New(), [2])], []);

        Assert.Equal("ledger-api · claude-work · Autonomous · 2 sessions", ReviewPhrases.Facts(history));
        Assert.Equal("ledger-api · 0 sessions", ReviewPhrases.Facts(history with { Summary = summary with { Connection = Option<ConnectionName>.None, Autonomy = Option<Autonomy>.None }, Sessions = [] }));
    }

    [Theory]
    [InlineData(JobStatus.AwaitingReview, "Ready for review")]
    [InlineData(JobStatus.Approved, "Approved")]
    [InlineData(JobStatus.Discarded, "Discarded")]
    [InlineData(JobStatus.NeedsHelp, "Needs help")]
    [InlineData(JobStatus.Running, "Running")]
    [InlineData(JobStatus.Preparing, "Not ready for review")]
    public void TheHeadingAboveTheTitleSaysWhereTheJobStands(JobStatus status, string heading) =>
        Assert.Equal(heading, ReviewPhrases.Heading(status));

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
