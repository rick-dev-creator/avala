using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Policies;

public sealed class FormPolicyTests
{
    private static readonly ItemId Item = new("question");

    private static readonly FormField Database = new(
        "database",
        "Database",
        "Which database?",
        FieldKind.SingleChoice,
        [new FormOption("SQLite", "A file."), new FormOption("PostgreSQL", "A server.", Recommended: true)],
        AcceptsFreeText: true);

    private static readonly FormField Region = new(
        "region",
        "Region",
        "Which region?",
        FieldKind.MultipleChoice,
        [new FormOption("eu", "Europe."), new FormOption("us", "America.")]);

    private static readonly FormField Name = new("name", "Name", "How should it be called?", FieldKind.FreeText, []);

    private static readonly FormField Approve = new("approve", "Plan", "Proceed?", FieldKind.Confirmation, []);

    private static readonly AgentForm Question = new(FormPurpose.Question, "Set up storage", "The service needs storage.", [Database, Region, Name, Approve]);

    [Fact]
    public void ASupervisedPolicyLeavesEveryFormToAHuman() =>
        Assert.True(Policy(Autonomy.Supervised, FormStrategy.Recommended).Answer(Item, Question).IsNone);

    [Fact]
    public void TheRecommendedStrategyTakesTheRecommendedOptionAndLeavesTheRestToTheAgentsJudgment()
    {
        var answered = Outcomes.Present(Policy(Autonomy.Autonomous, FormStrategy.Recommended).Answer(Item, Question));

        Assert.Equal(
            [
                ("database", AssumptionBasis.RecommendedOption, "PostgreSQL", ""),
                ("region", AssumptionBasis.FirstOption, "eu", ""),
                ("name", AssumptionBasis.AgentJudgment, "", FormPolicy.Judgment),
                ("approve", AssumptionBasis.Confirmed, "", ""),
            ],
            Summary(answered));
        Assert.Equal(["Which database?", "Which region?", "How should it be called?", "Proceed?"], answered.Assumptions.Select(assumption => assumption.Prompt));
        Assert.True(answered.Answer.Fields.Single(field => field.Field == "approve").Confirmed);
    }

    [Fact]
    public void TheBestJudgmentStrategyHandsEveryFieldThatTakesTextBackToTheAgent()
    {
        var answered = Outcomes.Present(Policy(Autonomy.Autonomous, FormStrategy.BestJudgment).Answer(Item, Question));

        Assert.Equal(
            [
                ("database", AssumptionBasis.AgentJudgment, "", FormPolicy.Judgment),
                ("region", AssumptionBasis.FirstOption, "eu", ""),
                ("name", AssumptionBasis.AgentJudgment, "", FormPolicy.Judgment),
                ("approve", AssumptionBasis.Confirmed, "", ""),
            ],
            Summary(answered));
    }

    [Fact]
    public void AnAutonomousPolicyDeclinesAPermissionAskedThroughAForm()
    {
        var form = Question with { Purpose = FormPurpose.Permission };

        var answered = Outcomes.Present(Policy(Autonomy.Autonomous, FormStrategy.Recommended).Answer(Item, form));

        Assert.Equal((true, Option<string>.Some(FormPolicy.Refusal), 0, 0), (answered.Answer.Declined, answered.Answer.Message, answered.Answer.Fields.Count, answered.Assumptions.Count));
    }

    private static PermissionPolicy Policy(Autonomy autonomy, FormStrategy strategy) => new([], autonomy, strategy);

    private static IEnumerable<(string, AssumptionBasis, string, string)> Summary(AutomaticAnswer answered) =>
        answered.Assumptions.Zip(answered.Answer.Fields, (assumption, field) => (
            assumption.Field,
            assumption.Basis,
            string.Join(",", field.Chosen),
            field.Text.Match(text => text, () => string.Empty)));
}
