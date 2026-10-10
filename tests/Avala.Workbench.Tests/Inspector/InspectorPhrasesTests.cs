using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class InspectorPhrasesTests
{
    private static readonly PolicyRule Remembered = new(RuleOrigin.Job, "don't ask again for this job", ItemKind.FileEdit, "src/auth/login.ts", RuleScope.Anywhere, PolicyAnswer.Allow);

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
    [InlineData("Allow", "", "", "You allowed FileEdit src/auth/login.ts")]
    [InlineData("Allow", "Session", "", "You allowed FileEdit src/auth/login.ts · don't ask again this session")]
    [InlineData("Allow", "Job", "", "You allowed FileEdit src/auth/login.ts · don't ask again for this job")]
    [InlineData("Allow", "Job", "Written", "You allowed FileEdit src/auth/login.ts · don't ask again for this job · always in this repository")]
    [InlineData("Deny", "Job", "Malformed", "You denied FileEdit src/auth/login.ts · don't ask again for this job · not added to the repository")]
    [InlineData("Deny", "", "", "You denied FileEdit src/auth/login.ts")]
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
            }));

    [Fact]
    public void TheHoldAtALimitReadsAsTheThresholdItHoldsAtAsTheUsagePageSaysIt() =>
        Assert.Equal(
            ["Cost cap USD 5", "Holds at 95% of a limit"],
            InspectorPhrases.Caps(new Budgets.Contracts.BudgetCaps([new Cost(5m, "USD")], Option<long>.None, 0.95)));

    [Theory]
    [InlineData("Deny", "Answered", "Denied Command rm -rf / · default")]
    [InlineData("Ask", "LeftToHuman", "Asked you Command rm -rf /")]
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
                "Asked you Command dotnet ef database update · unanswered",
                "Asked you Command dotnet ef database update · unanswered, its session ended",
                "Asked you Command dotnet ef database update",
                "Asked you Command dotnet ef database update · withdrawn by the harness",
            ],
            [
                InspectorPhrases.Decision(decision, [], asked),
                InspectorPhrases.Decision(decision, [], later),
                InspectorPhrases.Decision(decision, [answer], later),
                InspectorPhrases.Decision(decision with { Delivery = DecisionDelivery.Withdrawn }, [], asked),
            ]);
    }

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
