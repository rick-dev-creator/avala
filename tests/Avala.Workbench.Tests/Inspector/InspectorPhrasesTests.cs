using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class InspectorPhrasesTests
{
    private static readonly PolicyRule SessionRule = new(RuleOrigin.Session, "edits", ItemKind.FileEdit, "src/**", RuleScope.Anywhere, PolicyAnswer.Allow);

    [Theory]
    [InlineData("Allow", false, "You allowed FileEdit src/auth/login.ts")]
    [InlineData("Allow", true, "You allowed FileEdit src/auth/login.ts · don't ask again")]
    [InlineData("Deny", false, "You denied FileEdit src/auth/login.ts")]
    public void APersonsAnswerSaysWhatWasAnsweredAndWhetherToAskAgain(string answer, bool dontAskAgain, string phrase) =>
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
                dontAskAgain ? SessionRule : Option<PolicyRule>.None,
                DateTimeOffset.UnixEpoch)));

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
            ],
            [
                InspectorPhrases.Decision(decision, [], asked),
                InspectorPhrases.Decision(decision, [], later),
                InspectorPhrases.Decision(decision, [answer], later),
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
