using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.Tests.Looping;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Tests.Evidence;

public sealed class CleanEvidenceTests
{
    private static readonly JobId Job = JobId.New();

    private static readonly CheckEvidence Tests = new("tests", "dotnet test", CheckStatus.Passed, 0, TimeSpan.FromSeconds(1), string.Empty, string.Empty);

    [Fact]
    public void AVerifiedRunWithoutAnyExceptionUnderARuleThatAllowsItIsClean()
    {
        Assert.Empty(Clean.Exceptions);
        Assert.Equal(
            (2, Option<VerificationOutcome>.Some(VerificationOutcome.Passed), "tests", 1, 0, "GREETING.md"),
            (Clean.Summary.Attempts, Clean.Summary.Verification, string.Join(',', Clean.Summary.ChecksPassed), Clean.Summary.PermissionsAllowed, Clean.Summary.FormsDecided, string.Join(',', Clean.Summary.FilesChanged)));
    }

    public static TheoryData<string, string> Exceptional => new()
    {
        { "rules that never approve", nameof(ExceptionReason.NotDeclared) },
        { "rules that cannot be read", nameof(ExceptionReason.UnreadableRules) },
        { "no verification", nameof(ExceptionReason.VerificationNotPassed) },
        { "no checks declared", nameof(ExceptionReason.VerificationNotPassed) },
        { "a failed last verification", nameof(ExceptionReason.VerificationNotPassed) },
        { "a request denied by the policy", nameof(ExceptionReason.Denial) },
        { "a form declined", nameof(ExceptionReason.Denial) },
        { "an assumption", nameof(ExceptionReason.Assumption) },
        { "a rule file in the diff", nameof(ExceptionReason.RuleFileEdited) },
        { "a check declaration edited in the worktree", nameof(ExceptionReason.RuleFileEdited) },
        { "a policy edited in the worktree", nameof(ExceptionReason.RuleFileEdited) },
        { "a continuation after a hold", nameof(ExceptionReason.Held) },
        { "a diff that cannot be read", nameof(ExceptionReason.ChangesUnknown) },
    };

    [Theory]
    [MemberData(nameof(Exceptional))]
    public void EveryExceptionKeepsTheJobForAPerson(string exception, string reason) =>
        Assert.Equal([Enum.Parse<ExceptionReason>(reason)], With(exception).Exceptions);

    [Fact]
    public void AJobVerifiedAfterARetryIsStillCleanSinceOnlyItsLastVerificationCounts() =>
        Assert.Empty((Clean with { Verifications = [Report(VerificationOutcome.Failed), Report(VerificationOutcome.Passed, Tests)] }).Exceptions);

    [Fact]
    public void TheEvidenceOfARunNamesEveryExceptionalItemAndLeavesTheApprovalRuleToTheLoop()
    {
        var assumption = new Assumption("database", "Which database?", AssumptionBasis.RecommendedOption, ["PostgreSQL"]);
        var evidence = Clean with
        {
            Rules = AutopilotRules.Default,
            Verifications = [Report(VerificationOutcome.Passed, Tests) with { Declaration = new FileOrigin("ba5e", EditedInWorktree: true) }],
            Attempts = [Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted), Pilot.Attempt(2, AttemptOrigin.Hint, AttemptOutcome.Passed)],
            Decisions = [Decision(PolicyAnswer.Allow), Decision(PolicyAnswer.Allow), Decision(PolicyAnswer.Deny)],
            Forms = [Form(new FormAnswer(new ItemId("plan"), []), [assumption])],
            ChangedFiles = Option<IReadOnlyList<string>>.Some(["GREETING.md", ".avala/checks.json"]),
        };

        var run = evidence.Run(Job);

        Assert.Equal([ExceptionReason.Denial, ExceptionReason.Assumption, ExceptionReason.RuleFileEdited, ExceptionReason.Held], run.Exceptions);
        Assert.Equal(
            (PolicyAnswer.Deny, assumption, ".avala/checks.json", 2, 2),
            (Assert.Single(run.Denials).Answer, Assert.Single(Assert.Single(run.Assumed).Assumptions), Assert.Single(run.RuleFiles), Assert.Single(run.Holds).Number, run.AllowedByRules));
    }

    [Fact]
    public async Task AJobTheCatalogDoesNotKnowHasNoRunEvidence()
    {
        await using var pilot = new Pilot();
        var query = new RunEvidenceQuery(new EvidenceGatherer(pilot.Evidence, pilot.Evidence, pilot.Jobs, new JobWork(pilot.Work, pilot.Work, pilot.Rules)));

        Assert.True((await query.OfJobAsync(JobId.New(), TestContext.Current.CancellationToken)).IsNone);
    }

    private static JobEvidence Clean { get; } = new JobEvidence(
        Pilot.Clean,
        [Report(VerificationOutcome.Passed, Tests)],
        [Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Rejected), Pilot.Attempt(2, AttemptOrigin.Retry, AttemptOutcome.Passed)],
        Option<IReadOnlyList<string>>.Some(["GREETING.md"]))
    {
        Decisions = [Decision(PolicyAnswer.Allow)],
    };

    private static JobEvidence With(string exception) => exception switch
    {
        "rules that never approve" => Clean with { Rules = AutopilotRules.Default },
        "rules that cannot be read" => Clean with { Rules = Option<AutopilotRules>.None },
        "no verification" => Clean with { Verifications = [] },
        "no checks declared" => Clean with { Verifications = [Report(VerificationOutcome.NoChecksDeclared)] },
        "a failed last verification" => Clean with { Verifications = [Report(VerificationOutcome.Passed, Tests), Report(VerificationOutcome.Failed)] },
        "a request denied by the policy" => Clean with { Decisions = [Decision(PolicyAnswer.Allow), Decision(PolicyAnswer.Deny)] },
        "a form declined" => Clean with { Forms = [Form(new FormAnswer(new ItemId("plan"), []) { Declined = true }, [])] },
        "an assumption" => Clean with
        {
            Forms = [Form(new FormAnswer(new ItemId("plan"), []), [new Assumption("database", "Which database?", AssumptionBasis.RecommendedOption, ["PostgreSQL"])])],
        },
        "a rule file in the diff" => Clean with { ChangedFiles = Option<IReadOnlyList<string>>.Some(["GREETING.md", ".avala/budget.json"]) },
        "a check declaration edited in the worktree" => Clean with
        {
            Verifications = [Report(VerificationOutcome.Passed, Tests) with { Declaration = new FileOrigin("ba5e", EditedInWorktree: true) }],
        },
        "a policy edited in the worktree" => Clean with { RuleOrigins = [new FileOrigin("ba5e", EditedInWorktree: true)] },
        "a continuation after a hold" => Clean with
        {
            Attempts = [Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted), Pilot.Attempt(2, AttemptOrigin.Hint, AttemptOutcome.Passed)],
        },
        _ => Clean with { ChangedFiles = Option<IReadOnlyList<string>>.None },
    };

    private static VerificationReport Report(VerificationOutcome outcome, params CheckEvidence[] checks) => Pilot.Report(Job, outcome, checks);

    private static PolicyDecision Decision(PolicyAnswer answer) =>
        new(default, default, new ItemId("edit"), Job, ItemKind.FileEdit, "GREETING.md", answer, Option<PolicyRule>.None, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);

    private static FormDecision Form(FormAnswer answer, IReadOnlyList<Assumption> assumptions) =>
        new(default, default, new ItemId("plan"), Job, new AgentForm(FormPurpose.Question, "Plan", "Context", []), Autonomy.Autonomous, answer, assumptions, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);
}
