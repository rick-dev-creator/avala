using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Protocol;

internal static class Questions
{
    public const string AskTool = "AskUserQuestion";

    public const string PlanTool = "ExitPlanMode";

    private const string RecommendedMark = "(Recommended)";

    private const string ApprovalField = "approve";

    public static bool Asks(string tool) => tool is AskTool or PlanTool;

    public static AgentForm Form(string tool, JsonObject input) =>
        tool == PlanTool ? PlanForm(input) : QuestionForm(input);

    public static JsonObject Answered(string tool, JsonObject input, FormAnswer answer)
    {
        var updated = Json.Copy(input);

        if (tool == AskTool)
        {
            var answers = new JsonObject();

            foreach (var (question, index) in input.Items("questions").Select((question, index) => (question, index)))
            {
                answers[question.TextOr("question", FieldId(index))] = Reply(answer, FieldId(index));
            }

            updated["answers"] = answers;
        }

        return updated;
    }

    public static bool Approves(string tool, FormAnswer answer) =>
        !answer.Declined && (tool != PlanTool || answer.Fields.Any(field => field.Field == ApprovalField && field.Confirmed));

    public static string Refusal(string tool, FormAnswer answer) =>
        answer.Message.Match(
            message => message,
            () => tool == PlanTool
                ? string.Join(' ', ["The plan was not approved.", .. answer.Fields.SelectMany(field => field.Text.Match<string[]>(text => [text], () => []))])
                : "The user declined to answer.");

    private static AgentForm QuestionForm(JsonObject input)
    {
        var questions = input.Items("questions");
        var fields = questions.Select((question, index) => new FormField(
            FieldId(index),
            question.TextOr("header", $"Question {index + 1}"),
            question.TextOr("question", string.Empty),
            question.Flag("multiSelect") ? FieldKind.MultipleChoice : FieldKind.SingleChoice,
            Options(question),
            AcceptsFreeText: true)).ToList();

        return new AgentForm(
            FormPurpose.Question,
            fields.Count == 1 ? fields[0].Prompt : $"{fields.Count} questions",
            string.Empty,
            [.. fields.Select(field => field.Options.Count == 0 ? field with { Kind = FieldKind.FreeText, AcceptsFreeText = false } : field)]);
    }

    private static AgentForm PlanForm(JsonObject input) =>
        new(
            FormPurpose.PlanApproval,
            "Approve the plan",
            input.TextOr("plan", string.Empty),
            [new FormField(ApprovalField, "Plan", "Proceed with this plan?", FieldKind.Confirmation, [], AcceptsFreeText: true)]);

    private static List<FormOption> Options(JsonNode question)
    {
        var options = question.Items("options")
            .Select(option => (Label: option.TextOr("label", string.Empty), Description: option.TextOr("description", string.Empty)))
            .Where(option => !string.IsNullOrWhiteSpace(option.Label))
            .DistinctBy(option => option.Label, StringComparer.Ordinal)
            .ToList();
        var recommended = options.FindIndex(option => option.Label.EndsWith(RecommendedMark, StringComparison.OrdinalIgnoreCase));

        return [.. options.Select((option, index) => new FormOption(option.Label, option.Description, index == recommended))];
    }

    private static JsonValue Reply(FormAnswer answer, string field)
    {
        var given = answer.Fields.FirstOrDefault(candidate => candidate.Field == field);
        var parts = given is null ? [] : given.Chosen.Concat(given.Text.Match<string[]>(text => [text], () => [])).ToList();

        return JsonValue.Create(string.Join(", ", parts));
    }

    private static string FieldId(int index) => $"q{index + 1}";
}
