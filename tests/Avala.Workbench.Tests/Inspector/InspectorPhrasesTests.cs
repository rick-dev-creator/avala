using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Inspector;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Inspector;

public sealed class InspectorPhrasesTests
{
    private static readonly PolicyRule Remembered = new(RuleOrigin.Job, "don't ask again for this job", ItemKind.FileEdit, "src/auth/login.ts", RuleScope.Anywhere, PolicyAnswer.Allow);

    [Theory]
    [InlineData("avala/01a12983fa7477cebdae19e69c0ffee0", "main", "01a1298 from main")]
    [InlineData("avala/01a12983fa7477cebdae19e69c0ffee0", "", "01a1298")]
    [InlineData("avala/rate-limit-post-login", "", "rate-limit-post-login")]
    public void AWorktreeIsSummedUpByItsBranchsShortNameAndItsBase(string branch, string baseBranch, string phrase) =>
        Assert.Equal(phrase, InspectorPhrases.Worktree(branch, baseBranch.Length == 0 ? Option<string>.None : Option<string>.Some(baseBranch)));

    [Theory]
    [InlineData(AttemptOutcome.Running, null, "Attempt 1: the agent is working, no checks yet")]
    [InlineData(AttemptOutcome.AwaitingCheck, null, "Attempt 1: checks running")]
    [InlineData(AttemptOutcome.Interrupted, AttemptOrigin.Recovery, "Attempt 1: interrupted by a restart, no checks completed")]
    [InlineData(AttemptOutcome.Interrupted, AttemptOrigin.Hint, "Attempt 1: interrupted, no checks ran")]
    [InlineData(AttemptOutcome.Interrupted, null, "Attempt 1: interrupted, no checks ran")]
    [InlineData(AttemptOutcome.Passed, null, "Attempt 1: no checks ran")]
    public void AnAttemptWithoutAReportSaysWhyItHasNoEvidence(AttemptOutcome outcome, AttemptOrigin? next, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Attempts(
                [
                    new AttemptRecord(1, AttemptOrigin.Initial, outcome, Option<string>.None, Option<SessionId>.None),
                    .. next is { } origin ? [new AttemptRecord(2, origin, AttemptOutcome.Running, Option<string>.None, Option<SessionId>.None)] : Array.Empty<AttemptRecord>(),
                ],
                [])[0]);

    [Theory]
    [InlineData("Malformed", "Attempt 1: invalid declaration · it is not a JSON object with a checks array of check objects")]
    [InlineData("MissingCommand", "Attempt 1: invalid declaration · a check has no command")]
    [InlineData("InvalidTimeout", "Attempt 1: invalid declaration · a check has a timeout that is not a number of seconds between 0 and 86400")]
    [InlineData("Unreadable", "Attempt 1: invalid declaration · it could not be read from the job's commit")]
    [InlineData("", "Attempt 1: invalid declaration")]
    public void AnAttemptWithAnInvalidDeclarationSaysWhatIsWrongWithIt(string problem, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Attempt(new VerificationReport(
                JobId.New(),
                1,
                VerificationOutcome.InvalidDeclaration,
                Option<Workspaces.Contracts.FileOrigin>.None,
                [],
                GateVerdict.HoldFor(HoldReason.InvalidChecks),
                DateTimeOffset.UnixEpoch)
            {
                Problem = problem.Length == 0 ? Option<DeclarationProblem>.None : Enum.Parse<DeclarationProblem>(problem),
            }));

    [Theory]
    [InlineData("Allow", "", "", "You allowed: edit src/auth/login.ts")]
    [InlineData("Allow", "Session", "", "You allowed: edit src/auth/login.ts · don't ask again this session")]
    [InlineData("Allow", "Job", "", "You allowed: edit src/auth/login.ts · don't ask again for this job")]
    [InlineData("Allow", "Job", "Written", "You allowed: edit src/auth/login.ts · don't ask again for this job · always in this repository")]
    [InlineData("Deny", "Job", "Malformed", "You denied: edit src/auth/login.ts · don't ask again for this job · not added to the repository")]
    [InlineData("Deny", "", "", "You denied: edit src/auth/login.ts")]
    [InlineData("Allow", "", "Everything", "You allowed: edit src/auth/login.ts · everything in this job")]
    public void APersonsAnswerSaysWhatWasAnsweredAndHowLongItIsRemembered(string answer, string remembered, string repository, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Answer(new HumanAnswer(
                SessionId.New(),
                Option<JobId>.None,
                new ItemId("edit"),
                ItemKind.FileEdit,
                "src/auth/login.ts",
                Enum.Parse<PermissionAnswer>(answer),
                Option<string>.None,
                remembered switch
                {
                    "Session" => Remembered with { Origin = RuleOrigin.Session, Name = "don't ask again this session" },
                    "Job" => Remembered,
                    _ => Option<PolicyRule>.None,
                },
                DateTimeOffset.UnixEpoch)
            {
                RepositoryRule = repository == "Written" ? Remembered with { Origin = RuleOrigin.Repository, Name = "always in this repository" } : Option<PolicyRule>.None,
                RepositoryError = repository == "Malformed" ? PolicyError.Malformed : Option<PolicyError>.None,
                Everything = repository == "Everything",
            }));

    [Theory]
    [InlineData(CostBasis.Billed, CostBasis.Billed, "0.50 USD of 5 USD")]
    [InlineData(CostBasis.ApiEquivalent, CostBasis.ApiEquivalent, "0.50 USD at API prices of 5 USD")]
    [InlineData(CostBasis.Billed, CostBasis.ApiEquivalent, "0.50 USD partly at API prices of 5 USD")]
    public void SpendMeasuredAgainstACapSaysHowMuchOfItIsAtApiPrices(CostBasis first, CostBasis second, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Spending(
                new Observability.Contracts.UsageSummary(default, [new Cost(0.2m, "USD") { Basis = first }, new Cost(0.3m, "USD") { Basis = second }], 0, default, []),
                new Cost(5m, "USD")));

    [Fact]
    public void TheHoldAtALimitReadsAsTheThresholdItHoldsAtAsTheUsagePageSaysIt() =>
        Assert.Equal(
            ["Cost cap 5 USD", "Holds at 95% of a limit"],
            InspectorPhrases.Caps(new Budgets.Contracts.BudgetCaps([new Cost(5m, "USD")], Option<long>.None, 0.95)));

    [Theory]
    [InlineData("Deny", "Answered", "Denied: run rm -rf / · default")]
    [InlineData("Ask", "LeftToHuman", "Asked you: run rm -rf /")]
    public void APolicyDecisionWithoutARuleSaysWhetherTheDefaultAnswered(string answer, string delivery, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Decision(new PolicyDecision(
                SessionId.New(),
                TurnId.New(),
                new ItemId("run"),
                Option<JobId>.None,
                ItemKind.Command,
                "rm -rf /",
                Enum.Parse<PolicyAnswer>(answer),
                Option<PolicyRule>.None,
                Enum.Parse<DecisionDelivery>(delivery),
                DateTimeOffset.UnixEpoch)));

    [Fact]
    public void ADecisionLeftToYouSaysWhetherItIsUnansweredAndWhetherItsSessionHasEnded()
    {
        var (asked, later) = (SessionId.New(), SessionId.New());
        var decision = new PolicyDecision(asked, TurnId.New(), new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);
        var answer = new HumanAnswer(asked, Option<JobId>.None, new ItemId("migrate"), ItemKind.Command, "dotnet ef database update", PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch);

        Assert.Equal(
            [
                "Asked you: run dotnet ef database update · unanswered",
                "Asked you: run dotnet ef database update · unanswered, its session ended",
                "Asked you: run dotnet ef database update",
                "Asked you: run dotnet ef database update · withdrawn by the harness",
            ],
            [
                InspectorPhrases.Decision(decision, [], asked),
                InspectorPhrases.Decision(decision, [], later),
                InspectorPhrases.Decision(decision, [answer], later),
                InspectorPhrases.Decision(decision with { Delivery = DecisionDelivery.Withdrawn }, [], asked),
            ]);
    }

    [Fact]
    public void ARequestAskedOfTheParentSaysWhoAnsweredItAndAParentListsTheAnswersItGave()
    {
        var asked = SessionId.New();
        var parent = JobId.New();
        var decision = new PolicyDecision(asked, TurnId.New(), new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToParent, DateTimeOffset.UnixEpoch) { Parent = parent };
        var answer = new HumanAnswer(asked, Option<JobId>.None, new ItemId("migrate"), ItemKind.Command, "dotnet ef database update", PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch) { Parent = parent };

        Assert.Equal(
            [
                "Asked its parent: run dotnet ef database update · waiting for its parent",
                "Asked its parent: run dotnet ef database update",
                "Asked its parent: run dotnet ef database update · went to you, its parent did not answer in time · unanswered",
                "Its parent allowed: run dotnet ef database update",
                "Allowed for a sub-agent: run dotnet ef database update",
                " · 1 answered by its parent · 2 answers given to its sub-agents",
            ],
            [
                InspectorPhrases.Decision(decision, [], asked),
                InspectorPhrases.Decision(decision with { Delivery = DecisionDelivery.Answered }, [answer], asked),
                InspectorPhrases.Decision(decision with { Delivery = DecisionDelivery.LeftToHuman, Passed = PassReason.ParentTimedOut }, [], asked),
                InspectorPhrases.Answer(answer),
                InspectorPhrases.Given(answer),
                InspectorPhrases.ByParent(1, 2),
            ]);
    }

    [Fact]
    public void EveryKindOfRequestADecisionLineNamesIsWordedNeverItsCode()
    {
        var session = SessionId.New();
        var parent = JobId.New();

        var lines = Enum.GetValues<ItemKind>().SelectMany(kind =>
        {
            var answer = new HumanAnswer(session, Option<JobId>.None, new ItemId("asked"), kind, "target", PermissionAnswer.Deny, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch);

            return new[]
            {
                (Kind: kind, Line: InspectorPhrases.Decision(new PolicyDecision(session, TurnId.New(), new ItemId("asked"), Option<JobId>.None, kind, "target", PolicyAnswer.Deny, Option<PolicyRule>.None, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch))),
                (Kind: kind, Line: InspectorPhrases.Answer(answer)),
                (Kind: kind, Line: InspectorPhrases.Answer(answer with { Parent = parent })),
                (Kind: kind, Line: InspectorPhrases.Given(answer)),
            };
        });

        Assert.DoesNotContain(lines, line => Codes(line.Kind).Any(code => line.Line.Contains(code, StringComparison.Ordinal)));
    }

    [Fact]
    public void EveryCheckStatusAnAttemptLineNamesIsWordedNeverItsCode()
    {
        var lines = Enum.GetValues<CheckStatus>().Select(status => (Status: status, Line: InspectorPhrases.Attempt(new VerificationReport(
            JobId.New(),
            1,
            VerificationOutcome.Failed,
            Option<FileOrigin>.None,
            [new CheckEvidence("calculator", "./check.sh", status, status == CheckStatus.Skipped ? Option<int>.None : 1, TimeSpan.FromSeconds(1), string.Empty, string.Empty)],
            new GateVerdict(GateDecision.Retry, "checks failed"),
            DateTimeOffset.UnixEpoch))));

        Assert.DoesNotContain(lines, line => Codes(line.Status).Any(code => line.Line.Contains(code, StringComparison.Ordinal)));
    }

    private static string[] Codes(Enum value) =>
        value.ToString().Skip(1).Any(char.IsUpper) ? [value.ToString(), value.ToString().ToLowerInvariant()] : [value.ToString()];

    [Fact]
    public void AContestCandidatesCarveSaysItIsItsShareOfThePerJobBudget() =>
        Assert.Equal(
            "A contest candidate: 33% of the per-job budget · 0.33 USD",
            InspectorPhrases.Carve(new Avala.Budgets.Contracts.BudgetCarve(JobId.New(), JobId.New(), [new Cost(0.33m, "USD")], Option<long>.None, 1d / 3, DateTimeOffset.UnixEpoch) { Contested = true }));

    [Fact]
    public void AReadOnlyJobsAutonomySaysItIsReadOnly() =>
        Assert.Equal(
            "Autonomous, as the repository declares · read-only",
            InspectorPhrases.Autonomy(new SessionAutonomy(SessionId.New(), JobId.New(), Autonomy.Autonomous, Option<Autonomy>.None, Autonomy.Autonomous, Refused: false) { ReadOnly = true }));

    [Fact]
    public void AJobRaisedToEverythingSaysWhoAllowedItAndWhen() =>
        Assert.Equal(
            $"Everything allowed in this job by you at {Presenting.Amounts.Time(DateTimeOffset.UnixEpoch)}",
            InspectorPhrases.Autonomy(new SessionAutonomy(SessionId.New(), JobId.New(), Autonomy.Supervised, Option<Autonomy>.None, Autonomy.Autonomous, Refused: false) { Raised = DateTimeOffset.UnixEpoch }));

    [Theory]
    [InlineData("Supervised", "Supervised", false, "Supervised, as the repository declares")]
    [InlineData("Autonomous", "Supervised", false, "Supervised, tightened from the repository's Autonomous")]
    [InlineData("Supervised", "Supervised", true, "Supervised: asked for Autonomous, refused by the repository")]
    public void AnAutonomySaysWhereItCameFrom(string declared, string effective, bool refused, string phrase) =>
        Assert.Equal(
            phrase,
            InspectorPhrases.Autonomy(new SessionAutonomy(
                SessionId.New(),
                JobId.New(),
                Enum.Parse<Autonomy>(declared),
                refused ? Autonomy.Autonomous : Option<Autonomy>.None,
                Enum.Parse<Autonomy>(effective),
                refused)));
}
