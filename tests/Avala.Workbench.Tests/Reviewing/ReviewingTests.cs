using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Tests.Reviewing;

public sealed class ReviewingTests
{
    private static readonly JobId Job = JobId.New();

    private static readonly CheckEvidence Failing = new("calculator", "git grep", CheckStatus.Failed, 1, TimeSpan.FromSeconds(1), "add(2, 2) = 5", string.Empty);

    private static readonly CheckEvidence Passing = new("lint", "dotnet format", CheckStatus.Passed, 0, TimeSpan.FromSeconds(1), string.Empty, string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AnEarlierFailedAttemptAndEveryExceptionOfTheRunAreListedWithOnlyTheirFailingChecks()
    {
        var assumption = new Assumption("database", "Which database?", AssumptionBasis.RecommendedOption, ["PostgreSQL"]);
        var hint = new AttemptRecord(3, AttemptOrigin.Hint, AttemptOutcome.Passed, "Go on", Option<SessionId>.None);
        var run = Run(Report(1, VerificationOutcome.Failed, Failing, Passing), Report(3, VerificationOutcome.Passed, Passing)) with
        {
            Denials = [Denial()],
            Assumed = [Form([assumption])],
            Holds = [hint],
            RuleFiles = [".avala/checks.json"],
            Exceptions = [ExceptionReason.Denial, ExceptionReason.Assumption, ExceptionReason.Held, ExceptionReason.RuleFileEdited],
        };

        var exceptions = ReviewExceptions.Of(run);

        Assert.Equal(new ReviewVerdict(VerdictKind.Verified, 3, 3), ReviewExceptions.VerdictOf(run));
        Assert.Equal(
            [typeof(FailedAttempt), typeof(PolicyDenial), typeof(MadeAssumption), typeof(ContinuedAfterHold), typeof(EditedRuleFile)],
            exceptions.Select(exception => exception.GetType()));
        Assert.Equal([Failing], Assert.IsType<FailedAttempt>(exceptions[0]).Checks);
    }

    [Theory]
    [InlineData("Passed", "Verified")]
    [InlineData("Failed", "Failed")]
    [InlineData("NoChecksDeclared", "NoChecks")]
    [InlineData("InvalidDeclaration", "InvalidDeclaration")]
    [InlineData("", "Unverified")]
    public void TheVerdictIsTheLastVerification(string outcome, string verdict)
    {
        var run = outcome.Length == 0 ? Run() : Run(Report(2, Enum.Parse<VerificationOutcome>(outcome)));

        Assert.Equal(Enum.Parse<VerdictKind>(verdict), ReviewExceptions.VerdictOf(run).Kind);
    }

    [Fact]
    public async Task AMergeConflictIsReportedWithTheFilesThatConflict()
    {
        using var bench = new Bench();
        var job = bench.Job("Fix the failing test", JobStatus.AwaitingReview).Job;
        bench.Changes.Conflicts = ["calculator.txt"];
        bench.Jobs.Refusal = JobRejection.MergeConflict;

        var conflicted = await bench.Desk.ApproveAsync(job, Cancellation);
        bench.Jobs.Refusal = JobRejection.BaseCheckoutDirty;
        var dirty = await bench.Desk.ApproveAsync(job, Cancellation);

        Assert.Equal((JobRejection.MergeConflict, "calculator.txt"), (Outcomes.FailsWith(conflicted.Outcome), string.Join(",", conflicted.Conflicts)));
        Assert.Equal((JobRejection.BaseCheckoutDirty, 0), (Outcomes.FailsWith(dirty.Outcome), dirty.Conflicts.Count));
        Assert.Equal(["conflicts"], bench.Changes.Calls);
    }

    private static RunEvidence Run(params VerificationReport[] reports) =>
        new(Job, new EvidenceSummary(3, Option<VerificationOutcome>.None, [], 2, 0, []), []) { Verifications = reports };

    private static VerificationReport Report(int attempt, VerificationOutcome outcome, params CheckEvidence[] checks) =>
        new(Job, attempt, outcome, Option<Workspaces.Contracts.FileOrigin>.None, checks, GateVerdict.Pass, DateTimeOffset.UnixEpoch);

    private static PolicyDecision Denial() =>
        new(SessionId.New(), TurnId.New(), new ItemId("push"), Job, ItemKind.Command, "git push", PolicyAnswer.Deny, Option<PolicyRule>.None, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);

    private static FormDecision Form(IReadOnlyList<Assumption> assumptions) =>
        new(SessionId.New(), TurnId.New(), new ItemId("question"), Job, new AgentForm(FormPurpose.Question, "Database", "Context", []), Autonomy.Autonomous, Option<FormAnswer>.None, assumptions, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);
}
